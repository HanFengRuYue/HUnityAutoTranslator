<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from "vue";
import type { Component } from "vue";
import {
  BookOpenCheck,
  CheckCircle2,
  ChevronsUpDown,
  ClipboardPaste,
  Columns3,
  Copy,
  FileText,
  Filter,
  FilterX,
  Hash,
  Languages,
  Plus,
  RefreshCw,
  Save,
  Search,
  ShieldCheck,
  SortAsc,
  SortDesc,
  Sparkles,
  Trash2,
  XCircle
} from "lucide-vue-next";
import { deleteJson, getJson, patchJson, postJson } from "../api/client";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, saveConfig, setDirtyForm, showToast } from "../state/controlPanelStore";
import type {
  DeleteResult,
  GlossaryFilterOption,
  GlossaryFilterOptionPage,
  GlossaryTerm,
  GlossaryTermPage,
  GlossaryTermRequest,
  UpdateConfigRequest
} from "../types/api";
import { formatDateTime } from "../utils/format";
import {
  defaultGlossaryColumns,
  emptyFilterValue,
  glossaryCellValue,
  glossaryRowKey,
  loadGlossaryColumnFilters,
  loadGlossaryColumnOrder,
  loadGlossaryColumnWidths,
  loadGlossaryVisibleColumns,
  persistGlossaryColumnFilters,
  saveGlossaryColumnOrder,
  saveGlossaryColumnWidths,
  saveGlossaryVisibleColumns,
  type GlossaryTableColumn
} from "../utils/glossaryTable";
import { languageLabel, languageOptionsFor, normalizeLanguageInput, targetLanguageOptions } from "../utils/languages";

interface CellAddress {
  row: number;
  columnKey: GlossaryTableColumn["key"];
}

interface DirtyGlossaryRow {
  original: GlossaryTerm;
  current: GlossaryTerm;
}

type GlossaryAction = "refresh" | "copy" | "paste" | "enable" | "disable" | "delete" | "save";

const formKey = "glossary-settings";
const rows = ref<GlossaryTerm[]>([]);
const totalCount = ref(0);
const search = ref("");
const loading = ref(false);
const showInlineTermEditor = ref(false);
const glossarySourceTermInput = ref<HTMLInputElement | null>(null);
const sortColumn = ref("updated_utc");
const sortDirection = ref<"asc" | "desc">("desc");
const visibleKeys = ref(loadGlossaryVisibleColumns());
const orderKeys = ref(loadGlossaryColumnOrder());
const selectedCells = ref(new Set<string>());
const selectionAnchor = ref<CellAddress | null>(null);
const dirtyRows = reactive(new Map<string, DirtyGlossaryRow>());
const originalRows = reactive(new Map<string, GlossaryTerm>());
const originalRowKeys = ref<string[]>([]);
const columnMenuOpen = ref(false);
const tableMessage = ref("");
const columnFilters = reactive<Record<string, string[]>>(loadGlossaryColumnFilters());
const columnWidths = reactive<Record<string, number>>(loadGlossaryColumnWidths());
const contextMenu = reactive({
  open: false,
  x: 0,
  y: 0
});
const filterMenu = reactive({
  open: false,
  column: "",
  x: 0,
  y: 0,
  optionSearch: "",
  options: [] as GlossaryFilterOption[],
  draft: [] as string[]
});

const settings = reactive({
  EnableGlossary: true,
  EnableAutoTermExtraction: false,
  GlossaryMaxTerms: 16,
  GlossaryMaxCharacters: 1200
});

const inlineTerm = reactive({
  SourceTerm: "",
  TargetTerm: "",
  TargetLanguage: "zh-Hans",
  Note: "",
  Enabled: true
});

const settingsDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));
const orderedColumns = computed(() => {
  const byKey = new Map(defaultGlossaryColumns.map((column) => [column.key, column]));
  const ordered = orderKeys.value
    .map((key) => byKey.get(key as GlossaryTableColumn["key"]))
    .filter((column): column is GlossaryTableColumn => Boolean(column));
  return [
    ...ordered,
    ...defaultGlossaryColumns.filter((column) => !ordered.some((item) => item.key === column.key))
  ];
});
const visibleColumns = computed(() => orderedColumns.value.filter((column) => visibleKeys.value.includes(column.key)));
const visibleColumnSpan = computed(() => Math.max(visibleColumns.value.length, 1));
const selectedRows = computed(() => selectedRowIndexes().map((index) => rows.value[index]).filter((row): row is GlossaryTerm => Boolean(row)));
const hasColumnFilters = computed(() => Object.values(columnFilters).some((values) => values.length > 0));

function markSettingsDirty(): void {
  setDirtyForm(formKey, true);
}

function applyState(): void {
  const state = controlPanelStore.state;
  if (!state || settingsDirty.value) {
    return;
  }

  settings.EnableGlossary = state.EnableGlossary;
  settings.EnableAutoTermExtraction = state.EnableAutoTermExtraction;
  settings.GlossaryMaxTerms = state.GlossaryMaxTerms;
  settings.GlossaryMaxCharacters = state.GlossaryMaxCharacters;
  if (!inlineTerm.TargetLanguage) {
    inlineTerm.TargetLanguage = state.TargetLanguage || "zh-Hans";
  }
}

function readSettings(): UpdateConfigRequest {
  return {
    EnableGlossary: settings.EnableGlossary,
    EnableAutoTermExtraction: settings.EnableAutoTermExtraction,
    GlossaryMaxTerms: Number(settings.GlossaryMaxTerms),
    GlossaryMaxCharacters: Number(settings.GlossaryMaxCharacters)
  };
}

async function saveGlossarySettings(): Promise<void> {
  await saveConfig(readSettings(), formKey);
}

function appendColumnFilters(params: URLSearchParams, excludedColumn = ""): void {
  for (const [column, values] of Object.entries(columnFilters)) {
    if (column === excludedColumn || !values.length) {
      continue;
    }

    for (const value of values) {
      params.append(`filter.${column}`, value || emptyFilterValue);
    }
  }
}

function isGlossaryTermPage(value: unknown): value is GlossaryTermPage {
  if (!value || typeof value !== "object") {
    return false;
  }

  const page = value as Partial<GlossaryTermPage>;
  return Array.isArray(page.Items) && typeof page.TotalCount === "number";
}

function rememberOriginalRows(items: GlossaryTerm[]): void {
  originalRows.clear();
  originalRowKeys.value = items.map((row) => {
    const key = glossaryRowKey(row);
    originalRows.set(key, { ...row });
    return key;
  });
}

async function loadGlossaryTerms(): Promise<void> {
  loading.value = true;
  try {
    const params = new URLSearchParams({
      search: search.value,
      sort: sortColumn.value,
      direction: sortDirection.value,
      offset: "0",
      limit: "100"
    });
    appendColumnFilters(params);
    const page = await getJson<unknown>(`/api/glossary?${params.toString()}`);
    if (!isGlossaryTermPage(page)) {
      throw new Error("术语表返回格式无效。");
    }

    rows.value = page.Items;
    totalCount.value = page.TotalCount;
    rememberOriginalRows(page.Items);
    dirtyRows.clear();
    clearSelection();
  } catch (error) {
    showToast(error instanceof Error ? error.message : "术语加载失败。", "error");
  } finally {
    loading.value = false;
  }
}

function readInlineTerm(): GlossaryTermRequest {
  return {
    SourceTerm: inlineTerm.SourceTerm.trim(),
    TargetTerm: inlineTerm.TargetTerm.trim(),
    TargetLanguage: normalizeLanguageInput(inlineTerm.TargetLanguage) || controlPanelStore.state?.TargetLanguage || "zh-Hans",
    Note: inlineTerm.Note.trim() || null,
    Enabled: inlineTerm.Enabled
  };
}

function resetInlineTerm(): void {
  inlineTerm.SourceTerm = "";
  inlineTerm.TargetTerm = "";
  inlineTerm.TargetLanguage = controlPanelStore.state?.TargetLanguage || "zh-Hans";
  inlineTerm.Note = "";
  inlineTerm.Enabled = true;
}

function focusInlineTerm(): void {
  void nextTick(() => glossarySourceTermInput.value?.focus());
}

function beginAddGlossaryTerm(): void {
  resetInlineTerm();
  showInlineTermEditor.value = true;
  focusInlineTerm();
}

function toGlossaryTermRequest(row: GlossaryTerm, original?: GlossaryTerm): GlossaryTermRequest {
  return {
    SourceTerm: row.SourceTerm.trim(),
    TargetTerm: row.TargetTerm.trim(),
    TargetLanguage: normalizeLanguageInput(row.TargetLanguage) || controlPanelStore.state?.TargetLanguage || "zh-Hans",
    OriginalSourceTerm: original?.SourceTerm ?? null,
    OriginalTargetLanguage: original?.TargetLanguage ?? null,
    Note: row.Note?.trim() || null,
    Enabled: row.Enabled,
    UsageCount: row.UsageCount
  };
}

function isValidTermRequest(payload: GlossaryTermRequest): boolean {
  return Boolean(payload.SourceTerm?.trim() && payload.TargetTerm?.trim());
}

async function saveGlossaryTerm(term?: GlossaryTermRequest): Promise<void> {
  const payload = term ?? readInlineTerm();
  if (!isValidTermRequest(payload)) {
    showToast("请填写原术语和指定译名。", "warn");
    return;
  }

  const page = term
    ? await patchJson<GlossaryTermPage>("/api/glossary", payload)
    : await postJson<GlossaryTermPage>("/api/glossary", payload);
  rows.value = page.Items;
  totalCount.value = page.TotalCount;
  rememberOriginalRows(page.Items);
  dirtyRows.clear();
  clearSelection();
  resetInlineTerm();
  showInlineTermEditor.value = false;
  showToast("术语已保存。", "ok");
}

function rowOriginalKey(rowIndex: number): string {
  return originalRowKeys.value[rowIndex] ?? glossaryRowKey(rows.value[rowIndex]);
}

function originalForRow(rowIndex: number): GlossaryTerm | undefined {
  return originalRows.get(rowOriginalKey(rowIndex));
}

function editableValue(row: GlossaryTerm, key: GlossaryTableColumn["key"]): string | boolean | null {
  if (key === "Enabled") {
    return row.Enabled;
  }

  if (key === "TargetLanguage") {
    return normalizeLanguageInput(row.TargetLanguage);
  }

  return String(row[key] ?? "");
}

function rowsEqual(a: GlossaryTerm, b: GlossaryTerm): boolean {
  return a.Enabled === b.Enabled &&
    a.SourceTerm === b.SourceTerm &&
    a.TargetTerm === b.TargetTerm &&
    normalizeLanguageInput(a.TargetLanguage) === normalizeLanguageInput(b.TargetLanguage) &&
    (a.Note ?? "") === (b.Note ?? "");
}

function trackDirtyRow(rowIndex: number, row: GlossaryTerm): void {
  const key = rowOriginalKey(rowIndex);
  const original = originalRows.get(key) ?? row;
  if (rowsEqual(row, original)) {
    dirtyRows.delete(key);
  } else {
    dirtyRows.set(key, { original, current: row });
  }
}

function updateCellValue(rowIndex: number, column: GlossaryTableColumn, value: string | boolean): void {
  const existing = rows.value[rowIndex];
  if (!existing || !column.editable) {
    return;
  }

  const row = { ...existing };
  if (column.key === "Enabled") {
    row.Enabled = Boolean(value);
  } else if (column.key === "TargetLanguage") {
    row.TargetLanguage = normalizeLanguageInput(String(value));
  } else if (column.key === "Note") {
    row.Note = String(value);
  } else if (column.key === "SourceTerm" || column.key === "TargetTerm") {
    row[column.key] = String(value);
  }

  rows.value[rowIndex] = row;
  trackDirtyRow(rowIndex, row);
}

function updateCell(rowIndex: number, column: GlossaryTableColumn, event: Event): void {
  updateCellValue(rowIndex, column, (event.target as HTMLTextAreaElement | HTMLSelectElement).value);
}

function updateEnabled(rowIndex: number, column: GlossaryTableColumn, event: Event): void {
  updateCellValue(rowIndex, column, (event.target as HTMLInputElement).checked);
}

function isRowDirty(rowIndex: number): boolean {
  return dirtyRows.has(rowOriginalKey(rowIndex));
}

async function saveRows(): Promise<void> {
  if (!dirtyRows.size) {
    showToast("没有待保存的术语修改。", "info");
    return;
  }

  for (const change of dirtyRows.values()) {
    const payload = toGlossaryTermRequest(change.current, change.original);
    if (!isValidTermRequest(payload)) {
      showToast("待保存术语缺少原术语或指定译名。", "warn");
      return;
    }

    await patchJson<GlossaryTermPage>("/api/glossary", payload);
  }

  const savedCount = dirtyRows.size;
  dirtyRows.clear();
  await loadGlossaryTerms();
  tableMessage.value = `已保存 ${savedCount} 行修改。`;
  showToast("术语修改已保存。", "ok");
}

async function toggleGlossaryTerm(row: GlossaryTerm, enabled: boolean): Promise<void> {
  await saveGlossaryTerm(toGlossaryTermRequest({ ...row, Enabled: enabled }, row));
}

async function deleteGlossaryTerms(targets: GlossaryTerm[], confirmMessage: string): Promise<void> {
  if (!targets.length) {
    showToast("请先选择要删除的术语。", "warn");
    return;
  }

  if (!confirm(confirmMessage)) {
    return;
  }

  const result = await deleteJson<DeleteResult>("/api/glossary", targets.map((row) => toGlossaryTermRequest(row)));
  dirtyRows.clear();
  clearSelection();
  await loadGlossaryTerms();
  tableMessage.value = `已删除 ${result.DeletedCount || targets.length} 条术语。`;
  showToast("选中术语已删除。", "ok");
}

async function deleteGlossaryTerm(row: GlossaryTerm): Promise<void> {
  await deleteGlossaryTerms([row], `删除术语“${row.SourceTerm}”？`);
}

async function deleteSelectedGlossaryTerms(): Promise<void> {
  const targets = selectedRowIndexes()
    .map((rowIndex) => originalForRow(rowIndex) ?? rows.value[rowIndex])
    .filter((row): row is GlossaryTerm => Boolean(row));
  await deleteGlossaryTerms(targets, `删除选中的 ${targets.length} 条术语？`);
}

function toggleColumn(key: string, checked: boolean): void {
  visibleKeys.value = checked ? Array.from(new Set([...visibleKeys.value, key])) : visibleKeys.value.filter((item) => item !== key);
  saveGlossaryVisibleColumns(visibleKeys.value);
  clearSelection();
}

function showAllColumns(): void {
  visibleKeys.value = defaultGlossaryColumns.map((column) => column.key);
  saveGlossaryVisibleColumns(visibleKeys.value);
}

function moveColumn(key: string, direction: -1 | 1): void {
  const next = [...orderedColumns.value.map((column) => column.key)] as string[];
  const index = next.indexOf(key);
  const swapIndex = index + direction;
  if (index < 0 || swapIndex < 0 || swapIndex >= next.length) {
    return;
  }

  [next[index], next[swapIndex]] = [next[swapIndex], next[index]];
  orderKeys.value = next;
  saveGlossaryColumnOrder(next);
  clearSelection();
}

function setSort(column: GlossaryTableColumn): void {
  if (sortColumn.value === column.sort) {
    sortDirection.value = sortDirection.value === "asc" ? "desc" : "asc";
  } else {
    sortColumn.value = column.sort;
    sortDirection.value = "asc";
  }
  void loadGlossaryTerms();
}

function sortState(column: GlossaryTableColumn): "none" | "ascending" | "descending" {
  if (sortColumn.value !== column.sort) {
    return "none";
  }

  return sortDirection.value === "asc" ? "ascending" : "descending";
}

function ariaSort(column: GlossaryTableColumn): "none" | "ascending" | "descending" {
  return sortState(column);
}

function sortIcon(column: GlossaryTableColumn): Component {
  if (sortColumn.value !== column.sort) {
    return ChevronsUpDown;
  }

  return sortDirection.value === "asc" ? SortAsc : SortDesc;
}

function columnWidth(column: GlossaryTableColumn): number {
  return columnWidths[column.key] ?? column.width;
}

function clampColumnWidth(width: number): number {
  return Math.max(72, Math.min(640, width));
}

function startColumnResize(event: PointerEvent, column: GlossaryTableColumn): void {
  event.preventDefault();
  event.stopPropagation();
  const startX = event.clientX;
  const startWidth = columnWidth(column);
  const move = (moveEvent: PointerEvent): void => {
    columnWidths[column.key] = clampColumnWidth(startWidth + moveEvent.clientX - startX);
  };
  const finish = (): void => {
    document.removeEventListener("pointermove", move);
    document.removeEventListener("pointerup", finish);
    document.removeEventListener("pointercancel", finish);
    saveGlossaryColumnWidths(columnWidths);
  };
  document.addEventListener("pointermove", move);
  document.addEventListener("pointerup", finish);
  document.addEventListener("pointercancel", finish);
}

function cellKey(rowIndex: number, columnKey: GlossaryTableColumn["key"]): string {
  return `${rowIndex}:${columnKey}`;
}

function parseCellKey(key: string): CellAddress | null {
  const separator = key.indexOf(":");
  if (separator < 1) {
    return null;
  }

  const row = Number(key.slice(0, separator));
  if (!Number.isInteger(row)) {
    return null;
  }

  return { row, columnKey: key.slice(separator + 1) as GlossaryTableColumn["key"] };
}

function isCellSelected(rowIndex: number, column: GlossaryTableColumn): boolean {
  return selectedCells.value.has(cellKey(rowIndex, column.key));
}

function clearSelection(): void {
  selectedCells.value = new Set();
  selectionAnchor.value = null;
}

function replaceSelection(rowIndex: number, column: GlossaryTableColumn): void {
  selectedCells.value = new Set([cellKey(rowIndex, column.key)]);
  selectionAnchor.value = { row: rowIndex, columnKey: column.key };
}

function selectCell(rowIndex: number, column: GlossaryTableColumn, event: MouseEvent): void {
  const target = event.target as HTMLElement | null;
  if (!target?.closest(".cell-editor")) {
    document.getElementById("glossaryWrap")?.focus({ preventScroll: true });
  }

  if (event.shiftKey && selectionAnchor.value) {
    selectRange(selectionAnchor.value, { row: rowIndex, columnKey: column.key }, event.ctrlKey || event.metaKey);
    return;
  }

  const key = cellKey(rowIndex, column.key);
  const next = new Set(selectedCells.value);
  if (event.ctrlKey || event.metaKey) {
    if (next.has(key)) {
      next.delete(key);
    } else {
      next.add(key);
    }
  } else {
    next.clear();
    next.add(key);
  }

  selectedCells.value = next;
  selectionAnchor.value = { row: rowIndex, columnKey: column.key };
}

function selectRange(start: CellAddress, end: CellAddress, additive: boolean): void {
  const startColumnIndex = visibleColumns.value.findIndex((column) => column.key === start.columnKey);
  const endColumnIndex = visibleColumns.value.findIndex((column) => column.key === end.columnKey);
  if (startColumnIndex < 0 || endColumnIndex < 0) {
    return;
  }

  const next = additive ? new Set(selectedCells.value) : new Set<string>();
  const minRow = Math.min(start.row, end.row);
  const maxRow = Math.max(start.row, end.row);
  const minColumn = Math.min(startColumnIndex, endColumnIndex);
  const maxColumn = Math.max(startColumnIndex, endColumnIndex);
  for (let row = minRow; row <= maxRow; row++) {
    for (let columnIndex = minColumn; columnIndex <= maxColumn; columnIndex++) {
      const column = visibleColumns.value[columnIndex];
      if (column && rows.value[row]) {
        next.add(cellKey(row, column.key));
      }
    }
  }

  selectedCells.value = next;
}

function selectAllCells(): void {
  const next = new Set<string>();
  rows.value.forEach((_, rowIndex) => {
    visibleColumns.value.forEach((column) => {
      next.add(cellKey(rowIndex, column.key));
    });
  });
  selectedCells.value = next;
  const firstColumn = visibleColumns.value[0];
  selectionAnchor.value = firstColumn ? { row: 0, columnKey: firstColumn.key } : null;
}

function selectedCellAddresses(): CellAddress[] {
  const visibleOrder = new Map(visibleColumns.value.map((column, index) => [column.key, index]));
  return [...selectedCells.value]
    .map(parseCellKey)
    .filter((cell): cell is CellAddress => Boolean(cell))
    .filter((cell) => cell.row >= 0 && cell.row < rows.value.length && visibleOrder.has(cell.columnKey))
    .sort((a, b) => a.row - b.row || (visibleOrder.get(a.columnKey) ?? 0) - (visibleOrder.get(b.columnKey) ?? 0));
}

function selectedRowIndexes(): number[] {
  return [...new Set(selectedCellAddresses().map((cell) => cell.row))].sort((a, b) => a - b);
}

function firstSelectedCell(): CellAddress | null {
  return selectedCellAddresses()[0] ?? null;
}

function sourceLabel(value: string | null | undefined): string {
  if (value === "Automatic") {
    return "自动";
  }

  if (value === "Manual") {
    return "手动";
  }

  return value || "";
}

function displayCellValue(row: GlossaryTerm, column: GlossaryTableColumn): string {
  if (column.key === "Enabled") {
    return row.Enabled ? "启用" : "停用";
  }

  if (column.key === "TargetLanguage") {
    return languageLabel(row.TargetLanguage);
  }

  if (column.key === "Source") {
    return sourceLabel(row.Source);
  }

  if (column.key === "CreatedUtc" || column.key === "UpdatedUtc") {
    return formatDateTime(String(row[column.key] ?? ""));
  }

  return glossaryCellValue(row, column.key);
}

function clearSelectedEditableCells(): void {
  const cells = selectedCellAddresses();
  let clearedCells = 0;
  for (const cell of cells) {
    const column = visibleColumns.value.find((item) => item.key === cell.columnKey);
    if (!column?.editable || column.key === "Enabled" || !rows.value[cell.row]) {
      continue;
    }

    updateCellValue(cell.row, column, "");
    clearedCells++;
  }

  if (!clearedCells) {
    tableMessage.value = "没有选中的可编辑文本单元格。";
    showToast(tableMessage.value, "warn");
    return;
  }

  tableMessage.value = `已清空 ${clearedCells} 个单元格，等待保存。`;
  showToast(tableMessage.value, "ok");
}

async function copyCells(): Promise<void> {
  const cells = selectedCellAddresses();
  if (!cells.length) {
    tableMessage.value = "没有选中的单元格。";
    showToast(tableMessage.value, "warn");
    return;
  }

  const visibleOrder = new Map(visibleColumns.value.map((column, index) => [column.key, index]));
  const rowIndexes = [...new Set(cells.map((cell) => cell.row))].sort((a, b) => a - b);
  const columnKeys = [...new Set(cells.map((cell) => cell.columnKey))]
    .sort((a, b) => (visibleOrder.get(a) ?? 0) - (visibleOrder.get(b) ?? 0));
  const lines = rowIndexes.map((rowIndex) =>
    columnKeys.map((columnKey) => selectedCells.value.has(cellKey(rowIndex, columnKey)) ? displayCellValue(rows.value[rowIndex], visibleColumns.value.find((column) => column.key === columnKey)!) : "").join("\t")
  );
  await navigator.clipboard.writeText(lines.join("\n"));
  tableMessage.value = "已复制选区。";
  showToast(tableMessage.value, "ok");
}

function clipboardLines(text: string): string[] {
  const lines = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
  if (lines.length > 1 && lines[lines.length - 1] === "") {
    lines.pop();
  }
  return lines;
}

async function pasteCells(): Promise<void> {
  const start = firstSelectedCell();
  if (!start) {
    tableMessage.value = "请先选择粘贴起点。";
    showToast(tableMessage.value, "warn");
    return;
  }

  const startColumnIndex = visibleColumns.value.findIndex((column) => column.key === start.columnKey);
  if (startColumnIndex < 0) {
    tableMessage.value = "请先选择可见列中的粘贴起点。";
    showToast(tableMessage.value, "warn");
    return;
  }

  const text = await navigator.clipboard.readText();
  clipboardLines(text).forEach((line, rowOffset) => {
    line.split("\t").forEach((value, columnOffset) => {
      const rowIndex = start.row + rowOffset;
      const column = visibleColumns.value[startColumnIndex + columnOffset];
      if (rows.value[rowIndex] && column?.editable) {
        const nextValue = column.key === "TargetLanguage" ? normalizeLanguageInput(value) : value;
        updateCellValue(rowIndex, column, nextValue);
      }
    });
  });
  tableMessage.value = "已粘贴，等待保存。";
  showToast(tableMessage.value, "ok");
}

function setSelectedRowsEnabled(enabled: boolean): void {
  const indexes = selectedRowIndexes();
  if (!indexes.length) {
    showToast("请先选择要修改的术语。", "warn");
    return;
  }

  const column = defaultGlossaryColumns.find((item) => item.key === "Enabled");
  if (!column) {
    return;
  }

  indexes.forEach((rowIndex) => updateCellValue(rowIndex, column, enabled));
  tableMessage.value = enabled ? "已启用选中术语，等待保存。" : "已停用选中术语，等待保存。";
  showToast(tableMessage.value, "ok");
}

async function loadColumnFilterOptions(column: string, optionSearch = ""): Promise<void> {
  const params = new URLSearchParams({
    column,
    search: search.value,
    optionSearch,
    limit: "80"
  });
  appendColumnFilters(params, column);
  const page = await getJson<GlossaryFilterOptionPage>(`/api/glossary/filter-options?${params.toString()}`);
  filterMenu.options = page.Items;
}

function refreshFilterOptions(): void {
  void loadColumnFilterOptions(filterMenu.column, filterMenu.optionSearch);
}

function positionFilterMenu(anchor: HTMLElement): void {
  const rect = anchor.getBoundingClientRect();
  const margin = 12;
  const menuWidth = Math.min(320, window.innerWidth - margin * 2);
  const menuHeight = Math.min(360, window.innerHeight - margin * 2);
  const preferredLeft = rect.right - menuWidth;
  const preferredTop = rect.bottom + 8;
  const fallbackTop = rect.top - menuHeight - 8;

  filterMenu.x = Math.max(margin, Math.min(preferredLeft, window.innerWidth - menuWidth - margin));
  filterMenu.y = preferredTop + menuHeight <= window.innerHeight - margin
    ? Math.max(margin, preferredTop)
    : Math.max(margin, fallbackTop);
}

async function openColumnFilterMenu(column: GlossaryTableColumn, event: MouseEvent): Promise<void> {
  const anchor = event.currentTarget as HTMLElement | null;
  if (anchor) {
    positionFilterMenu(anchor);
  }
  filterMenu.open = true;
  filterMenu.column = column.sort;
  filterMenu.optionSearch = "";
  filterMenu.draft = [...(columnFilters[column.sort] ?? [])];
  await loadColumnFilterOptions(column.sort);
}

function hideColumnFilterMenu(): void {
  filterMenu.open = false;
}

function toggleFilterValue(value: string, checked: boolean): void {
  filterMenu.draft = checked
    ? Array.from(new Set([...filterMenu.draft, value]))
    : filterMenu.draft.filter((item) => item !== value);
}

function applyColumnFilter(column = filterMenu.column, values = filterMenu.draft): void {
  const clean = Array.from(new Set(values));
  if (clean.length) {
    columnFilters[column] = clean;
  } else {
    delete columnFilters[column];
  }
  persistGlossaryColumnFilters(columnFilters);
  hideColumnFilterMenu();
  void loadGlossaryTerms();
}

function hasColumnFilter(column: GlossaryTableColumn): boolean {
  return (columnFilters[column.sort] ?? []).length > 0;
}

function clearAllColumnFilters(): void {
  for (const key of Object.keys(columnFilters)) {
    delete columnFilters[key];
  }
  persistGlossaryColumnFilters(columnFilters);
  hideColumnFilterMenu();
  void loadGlossaryTerms();
}

function filterValueKey(value: string | null): string {
  return value ?? "";
}

function filterValueLabel(value: string | null): string {
  if (!value) {
    return "(空)";
  }

  if (filterMenu.column === "enabled") {
    return value === "true" ? "启用" : "停用";
  }

  if (filterMenu.column === "source") {
    return sourceLabel(value);
  }

  if (filterMenu.column === "target_language") {
    const label = languageLabel(value);
    return label === value ? value : `${label} (${value})`;
  }

  if (filterMenu.column === "created_utc" || filterMenu.column === "updated_utc") {
    return formatDateTime(value);
  }

  return value;
}

function showContextMenu(event: MouseEvent): void {
  event.preventDefault();
  contextMenu.x = Math.min(event.clientX, window.innerWidth - 220);
  contextMenu.y = Math.min(event.clientY, window.innerHeight - 260);
  contextMenu.open = true;
}

function openCellContextMenu(event: MouseEvent, rowIndex: number, column: GlossaryTableColumn): void {
  if (!isCellSelected(rowIndex, column)) {
    replaceSelection(rowIndex, column);
  }
  showContextMenu(event);
}

function hideContextMenu(): void {
  contextMenu.open = false;
}

async function handleTableAction(action: GlossaryAction): Promise<void> {
  hideContextMenu();
  if (action === "refresh") {
    await loadGlossaryTerms();
  } else if (action === "copy") {
    await copyCells();
  } else if (action === "paste") {
    await pasteCells();
  } else if (action === "enable") {
    setSelectedRowsEnabled(true);
  } else if (action === "disable") {
    setSelectedRowsEnabled(false);
  } else if (action === "delete") {
    await deleteSelectedGlossaryTerms();
  } else if (action === "save") {
    await saveRows();
  }
}

function handleTableKeydown(event: KeyboardEvent): void {
  const key = event.key.toLowerCase();
  if ((event.ctrlKey || event.metaKey) && key === "a") {
    event.preventDefault();
    selectAllCells();
  } else if ((event.ctrlKey || event.metaKey) && key === "c") {
    event.preventDefault();
    void copyCells();
  } else if ((event.ctrlKey || event.metaKey) && key === "v") {
    event.preventDefault();
    void pasteCells();
  } else if (event.key === "Delete" && selectedCells.value.size) {
    event.preventDefault();
    void clearSelectedEditableCells();
  }
}

function handleDocumentClick(event: MouseEvent): void {
  const target = event.target as HTMLElement | null;
  if (!target?.closest("#glossaryContextMenu")) {
    hideContextMenu();
  }

  if (!target?.closest("#glossaryColumnFilterMenu") && !target?.closest(".header-filter")) {
    hideColumnFilterMenu();
  }
}

watch(() => controlPanelStore.state, applyState, { immediate: true });
watch(search, () => {
  void loadGlossaryTerms();
});

onMounted(() => {
  void loadGlossaryTerms();
  document.addEventListener("click", handleDocumentClick);
});

onBeforeUnmount(() => {
  document.removeEventListener("click", handleDocumentClick);
});
</script>

<template>
  <section class="page active" id="page-glossary" data-page="glossary">
    <div class="page-head">
      <div>
        <h1>术语库</h1>
        <p>维护固定译名，并控制 AI 自动术语提取。</p>
      </div>
      <button class="primary" id="saveGlossarySettings" type="button" @click="saveGlossarySettings"><Save class="button-icon" />保存术语设置</button>
    </div>

    <div class="form-stack">
      <SectionPanel title="术语设置" :icon="BookOpenCheck">
        <div class="form-grid four" @input="markSettingsDirty" @change="markSettingsDirty">
          <label class="field help-target" data-help="限制每次请求最多注入多少条术语，设为 0 等于不注入术语。"><span class="field-label"><Hash class="field-label-icon" />注入术语上限</span><input id="glossaryMaxTerms" v-model.number="settings.GlossaryMaxTerms" type="number" min="0" max="100"></label>
          <label class="field help-target" data-help="限制注入提示词的术语总字符数，避免术语过多导致请求变长。"><span class="field-label"><FileText class="field-label-icon" />术语字符上限</span><input id="glossaryMaxCharacters" v-model.number="settings.GlossaryMaxCharacters" type="number" min="0" max="8000"></label>
        </div>
        <div class="checks" @change="markSettingsDirty">
          <label class="check help-target" data-help="把匹配到的术语作为强制译名写入提示词，手动术语优先。"><input id="enableGlossary" v-model="settings.EnableGlossary" type="checkbox"><ShieldCheck class="option-icon" />启用术语库约束</label>
          <label class="check help-target" data-help="允许 AI 从翻译缓存中抽取候选术语；默认关闭，启用后会增加额外请求。"><input id="enableAutoTermExtraction" v-model="settings.EnableAutoTermExtraction" type="checkbox"><Sparkles class="option-icon" />启用 AI 自动提取术语</label>
        </div>
        <p class="hint">AI 自动提取默认关闭；手动术语会优先进入提示词约束。</p>
      </SectionPanel>

      <SectionPanel title="术语条目" :icon="BookOpenCheck">
        <template #actions>
          <button id="addGlossaryTerm" class="secondary" type="button" @click="beginAddGlossaryTerm">
            <Plus class="button-icon" />
            新增术语
          </button>
          <button id="refreshGlossary" class="secondary" type="button" :disabled="loading" @click="loadGlossaryTerms">
            <RefreshCw class="button-icon" />
            {{ loading ? "刷新中" : "刷新" }}
          </button>
        </template>
        <div class="editor-tools">
          <label class="field search-field help-target" data-help="按原术语、指定译名、语言、来源或备注筛选当前术语列表。">
            <span class="field-label"><Search class="field-label-icon" />搜索</span>
            <input id="glossarySearch" v-model="search" placeholder="搜索原术语、指定译名、语言或备注">
          </label>
          <div class="editor-actions">
            <div class="column-control">
              <button id="glossaryColumnMenuButton" class="secondary" type="button" aria-controls="glossaryColumnChooser" :aria-expanded="columnMenuOpen" @click="columnMenuOpen = !columnMenuOpen"><Columns3 class="button-icon" />列显示</button>
              <div class="column-chooser" id="glossaryColumnChooser" :class="{ open: columnMenuOpen }">
                <div class="column-chooser-head">
                  <span>列显示</span>
                  <button id="showAllGlossaryColumns" type="button" @click="showAllColumns">全部显示</button>
                </div>
                <div class="column-chooser-list">
                  <div v-for="column in orderedColumns" :key="column.key" class="column-choice" :data-column-key="column.key">
                    <label class="check column-choice-label">
                      <input type="checkbox" :checked="visibleKeys.includes(column.key)" @change="toggleColumn(column.key, ($event.target as HTMLInputElement).checked)">
                      <span>{{ column.label }}</span>
                    </label>
                    <span class="column-move-buttons">
                      <button type="button" data-column-move="up" @click="moveColumn(column.key, -1)">↑</button>
                      <button type="button" data-column-move="down" @click="moveColumn(column.key, 1)">↓</button>
                    </span>
                  </div>
                </div>
              </div>
            </div>
            <button id="clearGlossaryFilters" class="secondary" type="button" :class="{ 'filter-active': hasColumnFilters }" @click="clearAllColumnFilters"><FilterX class="button-icon" />清空筛选</button>
            <button id="saveGlossaryRows" class="primary" type="button" :disabled="dirtyRows.size === 0" @click="saveRows"><Save class="button-icon" />保存修改</button>
            <button id="deleteSelectedGlossaryTerms" class="danger" type="button" :disabled="selectedRows.length === 0" @click="deleteSelectedGlossaryTerms"><Trash2 class="button-icon" />删除选中术语</button>
          </div>
        </div>

        <div class="table-wrap" id="glossaryWrap" tabindex="0" @contextmenu="showContextMenu" @keydown="handleTableKeydown">
          <table>
            <colgroup id="glossaryColgroup">
              <col v-for="column in visibleColumns" :key="column.key" :style="{ width: `${columnWidth(column)}px` }">
            </colgroup>
            <thead>
              <tr id="glossaryHead">
                <th v-for="column in visibleColumns" :key="column.key" :data-column-key="column.key" :aria-sort="ariaSort(column)" :data-sort-state="sortState(column)" :class="{ 'glossary-cell-center': column.align === 'center' }">
                  <div class="header-inner">
                    <button class="header-title" type="button" :title="`按 ${column.label} 排序`" @click="setSort(column)">
                      <span>{{ column.label }}</span>
                      <component :is="sortIcon(column)" class="sort-icon" aria-hidden="true" />
                    </button>
                    <button class="header-filter" type="button" aria-label="筛选" :title="`筛选 ${column.label}`" :class="{ 'filter-active': hasColumnFilter(column) }" :data-filter-column="column.sort" @click.stop="openColumnFilterMenu(column, $event)"><Filter class="table-icon" /></button>
                  </div>
                  <span class="col-resizer" :data-column-key="column.key" @pointerdown.stop.prevent="startColumnResize($event, column)"></span>
                </th>
              </tr>
            </thead>
            <tbody id="glossaryBody">
              <tr v-if="showInlineTermEditor" id="glossaryNewRow" class="inline-new-row">
                <td :colspan="visibleColumnSpan">
                  <div class="glossary-inline-editor">
                    <label class="check glossary-inline-check"><input v-model="inlineTerm.Enabled" class="compact-check" type="checkbox">启用</label>
                    <input id="glossarySourceTerm" ref="glossarySourceTermInput" v-model="inlineTerm.SourceTerm" autocomplete="off" placeholder="新增原术语">
                    <input id="glossaryTargetTerm" v-model="inlineTerm.TargetTerm" autocomplete="off" placeholder="指定译名">
                    <select id="glossaryTargetLanguage" v-model="inlineTerm.TargetLanguage">
                      <option v-for="option in targetLanguageOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
                    </select>
                    <input id="glossaryNote" v-model="inlineTerm.Note" autocomplete="off" placeholder="备注">
                    <button id="saveGlossaryInlineRow" type="button" @click="saveGlossaryTerm()"><Save class="button-icon" />保存</button>
                    <button class="secondary" type="button" @click="resetInlineTerm">清空</button>
                  </div>
                </td>
              </tr>
              <tr v-for="(row, rowIndex) in rows" :key="rowOriginalKey(rowIndex)" :class="{ dirty: isRowDirty(rowIndex) }">
                <td
                  v-for="column in visibleColumns"
                  :key="column.key"
                  :data-cell="cellKey(rowIndex, column.key)"
                  :data-row-index="rowIndex"
                  :data-column-key="column.key"
                  :class="{ selected: isCellSelected(rowIndex, column), dirty: isRowDirty(rowIndex) && column.editable, 'glossary-cell-center': column.align === 'center' }"
                  :title="column.key === 'TargetLanguage' ? languageLabel(row.TargetLanguage) : displayCellValue(row, column)"
                  @click="selectCell(rowIndex, column, $event)"
                  @contextmenu.stop.prevent="openCellContextMenu($event, rowIndex, column)"
                >
                  <input
                    v-if="column.key === 'Enabled'"
                    class="compact-check cell-check"
                    type="checkbox"
                    :checked="row.Enabled"
                    @click.stop="replaceSelection(rowIndex, column)"
                    @focus="replaceSelection(rowIndex, column)"
                    @change="updateEnabled(rowIndex, column, $event)"
                  >
                  <select
                    v-else-if="column.key === 'TargetLanguage'"
                    class="cell-editor"
                    :value="normalizeLanguageInput(row.TargetLanguage)"
                    @mousedown.stop
                    @click.stop="replaceSelection(rowIndex, column)"
                    @focus="replaceSelection(rowIndex, column)"
                    @keydown.stop
                    @change="updateCell(rowIndex, column, $event)"
                  >
                    <option v-for="option in languageOptionsFor(row.TargetLanguage)" :key="option.value" :value="option.value">{{ option.label }}</option>
                  </select>
                  <textarea
                    v-else-if="column.editable"
                    class="cell-editor"
                    :value="String(editableValue(row, column.key) ?? '')"
                    :spellcheck="false"
                    @mousedown.stop
                    @click.stop="replaceSelection(rowIndex, column)"
                    @focus="replaceSelection(rowIndex, column)"
                    @keydown.stop
                    @input="updateCell(rowIndex, column, $event)"
                  ></textarea>
                  <div v-else class="cell-text">{{ displayCellValue(row, column) }}</div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="message" id="glossaryMessage">
          共 {{ totalCount }} 条术语，当前显示 {{ rows.length }} 条。<span v-if="dirtyRows.size"> 待保存 {{ dirtyRows.size }} 行。</span> {{ tableMessage }}
        </div>
      </SectionPanel>
    </div>

    <div
      class="context-menu"
      id="glossaryContextMenu"
      :class="{ open: contextMenu.open }"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      @click.stop
    >
      <button data-glossary-action="refresh" type="button" @click="handleTableAction('refresh')"><RefreshCw class="button-icon" />刷新表格</button>
      <button data-glossary-action="copy" type="button" @click="handleTableAction('copy')"><Copy class="button-icon" />复制选区</button>
      <button data-glossary-action="paste" type="button" @click="handleTableAction('paste')"><ClipboardPaste class="button-icon" />粘贴到选区</button>
      <button data-glossary-action="enable" type="button" @click="handleTableAction('enable')"><CheckCircle2 class="button-icon" />启用选中</button>
      <button data-glossary-action="disable" type="button" @click="handleTableAction('disable')"><XCircle class="button-icon" />停用选中</button>
      <button data-glossary-action="delete" class="danger" type="button" @click="handleTableAction('delete')"><Trash2 class="button-icon" />删除选中术语</button>
      <button data-glossary-action="save" type="button" @click="handleTableAction('save')"><Save class="button-icon" />保存修改</button>
    </div>

    <div
      class="column-filter-menu"
      id="glossaryColumnFilterMenu"
      :class="{ open: filterMenu.open }"
      :style="{ left: `${filterMenu.x}px`, top: `${filterMenu.y}px` }"
      role="dialog"
      aria-label="术语列筛选"
      @click.stop
    >
      <input id="glossaryColumnFilterSearch" v-model="filterMenu.optionSearch" placeholder="搜索筛选值" @input="refreshFilterOptions">
      <div class="filter-option-list">
        <label v-for="option in filterMenu.options" :key="filterValueKey(option.Value)" class="filter-option-row">
          <input type="checkbox" :checked="filterMenu.draft.includes(filterValueKey(option.Value))" @change="toggleFilterValue(filterValueKey(option.Value), ($event.target as HTMLInputElement).checked)">
          <span>{{ filterValueLabel(option.Value) }}</span>
          <small>{{ option.Count }}</small>
        </label>
      </div>
      <div class="actions inline-actions">
        <button id="clearGlossaryColumnFilter" class="secondary" type="button" @click="applyColumnFilter(filterMenu.column, [])">清空</button>
        <button id="applyGlossaryColumnFilter" type="button" @click="applyColumnFilter()">应用</button>
      </div>
    </div>
  </section>
</template>
