<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from "vue";
import type { Component } from "vue";
import {
  ChevronsUpDown,
  ClipboardPaste,
  Columns3,
  Copy,
  Download,
  Filter,
  FilterX,
  MapPin,
  RefreshCw,
  Save,
  Search,
  SortAsc,
  SortDesc,
  Trash2,
  Upload
} from "lucide-vue-next";
import { api, buildQuery, deleteJson, getJson, getText, patchJson, postJson } from "../api/client";
import SectionPanel from "../components/SectionPanel.vue";
import { refreshState, showToast } from "../state/controlPanelStore";
import type {
  DeleteResult,
  RetranslateResult,
  TranslationCacheEntry,
  TranslationCacheFilterOption,
  TranslationCacheFilterOptionPage,
  TranslationCacheImportResult,
  TranslationCachePage,
  TranslationHighlightResult
} from "../types/api";
import { formatDateTime } from "../utils/format";
import {
  cellValue,
  defaultColumns,
  emptyFilterValue,
  loadColumnFilters,
  loadColumnOrder,
  loadColumnWidths,
  loadVisibleColumns,
  persistColumnFilters,
  rowKey,
  saveColumnOrder,
  saveColumnWidths,
  saveVisibleColumns,
  type TableColumn
} from "../utils/table";

interface CellAddress {
  row: number;
  columnKey: TableColumn["key"];
}

type TableAction = "refresh" | "copy" | "paste" | "retranslate" | "highlight" | "delete" | "save";

const rows = ref<TranslationCacheEntry[]>([]);
const totalCount = ref(0);
const search = ref("");
const loading = ref(false);
const sortColumn = ref("updated_utc");
const sortDirection = ref<"asc" | "desc">("desc");
const visibleKeys = ref(loadVisibleColumns());
const orderKeys = ref(loadColumnOrder());
const selectedCells = ref(new Set<string>());
const selectionAnchor = ref<CellAddress | null>(null);
const dirtyRows = reactive(new Map<string, TranslationCacheEntry>());
const columnMenuOpen = ref(false);
const exportMenuOpen = ref(false);
const importFile = ref<HTMLInputElement | null>(null);
const tableMessage = ref("");
const columnFilters = reactive<Record<string, string[]>>(loadColumnFilters());
const columnWidths = reactive<Record<string, number>>(loadColumnWidths());
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
  options: [] as TranslationCacheFilterOption[],
  draft: [] as string[]
});

const orderedColumns = computed(() => {
  const byKey = new Map(defaultColumns.map((column) => [column.key, column]));
  const ordered = orderKeys.value
    .map((key) => byKey.get(key as TableColumn["key"]))
    .filter((column): column is TableColumn => Boolean(column));
  return [
    ...ordered,
    ...defaultColumns.filter((column) => !ordered.some((item) => item.key === column.key))
  ];
});

const visibleColumns = computed(() => orderedColumns.value.filter((column) => visibleKeys.value.includes(column.key)));
const selectedRows = computed(() => selectedRowIndexes().map((index) => rows.value[index]).filter((row): row is TranslationCacheEntry => Boolean(row)));
const hasColumnFilters = computed(() => Object.values(columnFilters).some((values) => values.length > 0));

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

function clearSelection(): void {
  selectedCells.value = new Set();
  selectionAnchor.value = null;
}

function isTranslationCachePage(value: unknown): value is TranslationCachePage {
  if (!value || typeof value !== "object") {
    return false;
  }

  const page = value as Partial<TranslationCachePage>;
  return Array.isArray(page.Items) && typeof page.TotalCount === "number";
}

async function loadTranslations(): Promise<void> {
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
    const page = await getJson<unknown>(`/api/translations?${params.toString()}`);
    if (!isTranslationCachePage(page)) {
      throw new Error("翻译表返回格式无效");
    }

    rows.value = page.Items;
    totalCount.value = page.TotalCount;
    clearSelection();
  } catch (error) {
    showToast(error instanceof Error ? error.message : "翻译表加载失败", "error");
  } finally {
    loading.value = false;
  }
}

function toggleColumn(key: string, checked: boolean): void {
  visibleKeys.value = checked ? Array.from(new Set([...visibleKeys.value, key])) : visibleKeys.value.filter((item) => item !== key);
  saveVisibleColumns(visibleKeys.value);
  clearSelection();
}

function showAllColumns(): void {
  visibleKeys.value = defaultColumns.map((column) => column.key);
  saveVisibleColumns(visibleKeys.value);
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
  saveColumnOrder(next);
  clearSelection();
}

function setSort(column: TableColumn): void {
  if (sortColumn.value === column.sort) {
    sortDirection.value = sortDirection.value === "asc" ? "desc" : "asc";
  } else {
    sortColumn.value = column.sort;
    sortDirection.value = "asc";
  }
  void loadTranslations();
}

function sortState(column: TableColumn): "none" | "ascending" | "descending" {
  if (sortColumn.value !== column.sort) {
    return "none";
  }

  return sortDirection.value === "asc" ? "ascending" : "descending";
}

function ariaSort(column: TableColumn): "none" | "ascending" | "descending" {
  return sortState(column);
}

function sortIcon(column: TableColumn): Component {
  if (sortColumn.value !== column.sort) {
    return ChevronsUpDown;
  }

  return sortDirection.value === "asc" ? SortAsc : SortDesc;
}

function columnWidth(column: TableColumn): number {
  return columnWidths[column.key] ?? column.width;
}

function clampColumnWidth(width: number): number {
  return Math.max(72, Math.min(640, width));
}

function startColumnResize(event: PointerEvent, column: TableColumn): void {
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
    saveColumnWidths(columnWidths);
  };
  document.addEventListener("pointermove", move);
  document.addEventListener("pointerup", finish);
  document.addEventListener("pointercancel", finish);
}

function cellKey(rowIndex: number, columnKey: TableColumn["key"]): string {
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

  return { row, columnKey: key.slice(separator + 1) as TableColumn["key"] };
}

function isCellSelected(rowIndex: number, column: TableColumn): boolean {
  return selectedCells.value.has(cellKey(rowIndex, column.key));
}

function replaceSelection(rowIndex: number, column: TableColumn): void {
  selectedCells.value = new Set([cellKey(rowIndex, column.key)]);
  selectionAnchor.value = { row: rowIndex, columnKey: column.key };
}

function selectCell(rowIndex: number, column: TableColumn, event: MouseEvent): void {
  const target = event.target as HTMLElement | null;
  if (!target?.closest(".cell-editor")) {
    document.getElementById("tableWrap")?.focus({ preventScroll: true });
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

function displayCellValue(row: TranslationCacheEntry, column: TableColumn): string {
  const value = cellValue(row, column.key);
  return column.key.endsWith("Utc") ? formatDateTime(value) : value;
}

function updateCellValue(rowIndex: number, column: TableColumn, value: string): void {
  const existing = rows.value[rowIndex];
  if (!existing || !column.editable) {
    return;
  }

  const row = { ...existing };
  (row as unknown as Record<string, string | null>)[column.key] = value;
  rows.value[rowIndex] = row;
  dirtyRows.set(rowKey(row), row);
}

function updateCell(rowIndex: number, column: TableColumn, event: Event): void {
  updateCellValue(rowIndex, column, (event.target as HTMLTextAreaElement).value);
}

function clearSelectedEditableCells(): void {
  const cells = selectedCellAddresses();
  let clearedCells = 0;
  for (const cell of cells) {
    const column = visibleColumns.value.find((item) => item.key === cell.columnKey);
    if (!column?.editable || !rows.value[cell.row]) {
      continue;
    }

    updateCellValue(cell.row, column, "");
    clearedCells++;
  }

  if (!clearedCells) {
    tableMessage.value = "没有选中的可编辑单元格。";
    showToast("没有选中的可编辑单元格。", "warn");
    return;
  }

  tableMessage.value = `已清空 ${clearedCells} 个单元格，等待保存。`;
  showToast(tableMessage.value, "ok");
}

async function copyCells(): Promise<void> {
  const cells = selectedCellAddresses();
  if (!cells.length) {
    tableMessage.value = "没有选中的单元格。";
    showToast("没有选中的单元格。", "warn");
    return;
  }

  const visibleOrder = new Map(visibleColumns.value.map((column, index) => [column.key, index]));
  const rowIndexes = [...new Set(cells.map((cell) => cell.row))].sort((a, b) => a - b);
  const columnKeys = [...new Set(cells.map((cell) => cell.columnKey))]
    .sort((a, b) => (visibleOrder.get(a) ?? 0) - (visibleOrder.get(b) ?? 0));
  const lines = rowIndexes.map((rowIndex) =>
    columnKeys.map((columnKey) => selectedCells.value.has(cellKey(rowIndex, columnKey)) ? cellValue(rows.value[rowIndex], columnKey) : "").join("\t")
  );
  await navigator.clipboard.writeText(lines.join("\n"));
  tableMessage.value = "已复制选区。";
  showToast("已复制选区。", "ok");
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
    showToast("请先选择粘贴起点。", "warn");
    return;
  }

  const startColumnIndex = visibleColumns.value.findIndex((column) => column.key === start.columnKey);
  if (startColumnIndex < 0) {
    tableMessage.value = "请先选择可见列中的粘贴起点。";
    showToast("请先选择可见列中的粘贴起点。", "warn");
    return;
  }

  const text = await navigator.clipboard.readText();
  clipboardLines(text).forEach((line, rowOffset) => {
    line.split("\t").forEach((value, columnOffset) => {
      const rowIndex = start.row + rowOffset;
      const column = visibleColumns.value[startColumnIndex + columnOffset];
      if (rows.value[rowIndex] && column?.editable) {
        updateCellValue(rowIndex, column, value);
      }
    });
  });
  tableMessage.value = "已粘贴，等待保存。";
  showToast("已粘贴，等待保存。", "ok");
}

async function saveRows(): Promise<void> {
  for (const row of dirtyRows.values()) {
    await patchJson<TranslationCachePage>("/api/translations", row);
  }
  const savedCount = dirtyRows.size;
  dirtyRows.clear();
  await loadTranslations();
  await refreshState({ quiet: true });
  tableMessage.value = `已保存 ${savedCount} 行修改。`;
  showToast("修改已保存。", "ok");
}

async function retranslateSelectedRows(): Promise<void> {
  if (!selectedRows.value.length) {
    tableMessage.value = "没有选中的已翻译文本。";
    showToast("请先选择要重翻的行。", "warn");
    return;
  }

  const result = await postJson<RetranslateResult>("/api/translations/retranslate", selectedRows.value);
  tableMessage.value = `已提交 ${result.QueuedCount}/${result.RequestedCount} 行重新翻译。`;
  showToast("已提交重翻任务。", "ok");
}

async function highlightSelectedRow(): Promise<void> {
  const row = selectedRows.value[0];
  if (!row) {
    tableMessage.value = "没有选中的已翻译文本。";
    showToast("请先选择一行。", "warn");
    return;
  }

  const result = await postJson<TranslationHighlightResult>("/api/translations/highlight", row);
  tableMessage.value = result.Message || "已发送高亮请求。";
  showToast(tableMessage.value, result.Status === "queued" ? "ok" : "warn");
}

async function deleteSelectedRows(): Promise<void> {
  if (!selectedRows.value.length) {
    tableMessage.value = "没有选中的已翻译文本。";
    showToast("请先选择要删除的行。", "warn");
    return;
  }

  if (!confirm(`删除选中的 ${selectedRows.value.length} 行已翻译文本？`)) {
    return;
  }

  const result = await deleteJson<DeleteResult>("/api/translations", selectedRows.value);
  dirtyRows.clear();
  clearSelection();
  await loadTranslations();
  tableMessage.value = `已删除 ${result.DeletedCount} 行。`;
  showToast("选中行已删除。", "ok");
}

function openImportPicker(): void {
  if (importFile.value) {
    importFile.value.value = "";
  }
  importFile.value?.click();
}

async function importRows(): Promise<void> {
  const file = importFile.value?.files?.[0];
  if (!file) {
    return;
  }

  const text = await file.text();
  const format = file.name.toLowerCase().endsWith(".csv") ? "csv" : "json";
  const result = await api<TranslationCacheImportResult>(buildQuery("/api/translations/import", { format }), {
    method: "POST",
    body: text
  });
  await loadTranslations();
  tableMessage.value = `已导入 ${result.ImportedCount} 行。${result.Errors.length ? ` ${result.Errors.length} 个错误。` : ""}`;
  showToast(tableMessage.value, result.Errors.length ? "warn" : "ok");
  if (importFile.value) {
    importFile.value.value = "";
  }
}

async function exportRows(format: "json" | "csv"): Promise<void> {
  exportMenuOpen.value = false;
  const text = await getText(buildQuery("/api/translations/export", { format }));
  const blob = new Blob([text], { type: format === "csv" ? "text/csv;charset=utf-8" : "application/json;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `hunity-translations.${format}`;
  anchor.click();
  URL.revokeObjectURL(url);
  showToast("导出已开始。", "ok");
}

async function loadColumnFilterOptions(column: string, optionSearch = ""): Promise<void> {
  const params = new URLSearchParams({
    column,
    search: search.value,
    optionSearch,
    limit: "80"
  });
  appendColumnFilters(params, column);
  const page = await getJson<TranslationCacheFilterOptionPage>(`/api/translations/filter-options?${params.toString()}`);
  filterMenu.options = page.Items;
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

async function openColumnFilterMenu(column: TableColumn, event: MouseEvent): Promise<void> {
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
  persistColumnFilters(columnFilters);
  hideColumnFilterMenu();
  void loadTranslations();
}

function hasColumnFilter(column: TableColumn): boolean {
  return (columnFilters[column.sort] ?? []).length > 0;
}

function clearAllColumnFilters(): void {
  for (const key of Object.keys(columnFilters)) {
    delete columnFilters[key];
  }
  persistColumnFilters(columnFilters);
  hideColumnFilterMenu();
  void loadTranslations();
}

function filterValueKey(value: string | null): string {
  return value ?? "";
}

function filterValueLabel(value: string | null): string {
  return value && value.length ? value : "(空)";
}

function showContextMenu(event: MouseEvent): void {
  event.preventDefault();
  contextMenu.x = Math.min(event.clientX, window.innerWidth - 220);
  contextMenu.y = Math.min(event.clientY, window.innerHeight - 260);
  contextMenu.open = true;
}

function openCellContextMenu(event: MouseEvent, rowIndex: number, column: TableColumn): void {
  if (!isCellSelected(rowIndex, column)) {
    replaceSelection(rowIndex, column);
  }
  showContextMenu(event);
}

function hideContextMenu(): void {
  contextMenu.open = false;
}

async function handleTableAction(action: TableAction): Promise<void> {
  hideContextMenu();
  if (action === "refresh") {
    await loadTranslations();
  } else if (action === "copy") {
    await copyCells();
  } else if (action === "paste") {
    await pasteCells();
  } else if (action === "retranslate") {
    await retranslateSelectedRows();
  } else if (action === "highlight") {
    await highlightSelectedRow();
  } else if (action === "delete") {
    await deleteSelectedRows();
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
  if (!target?.closest("#tableContextMenu")) {
    hideContextMenu();
  }

  if (!target?.closest("#columnFilterMenu") && !target?.closest(".header-filter")) {
    hideColumnFilterMenu();
  }
}

onMounted(() => {
  void loadTranslations();
  document.addEventListener("click", handleDocumentClick);
});

onBeforeUnmount(() => {
  document.removeEventListener("click", handleDocumentClick);
});
</script>

<template>
  <section class="page active" id="page-editor">
    <div class="page-head">
      <div>
        <h1>文本编辑</h1>
        <p>查看、编辑和批量处理本地 SQLite 翻译缓存。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="loading" @click="loadTranslations"><RefreshCw class="button-icon" />{{ loading ? "刷新中" : "刷新" }}</button>
        <button class="primary" id="saveRows" type="button" :disabled="dirtyRows.size === 0" @click="saveRows"><Save class="button-icon" />保存修改</button>
      </div>
    </div>

    <SectionPanel title="翻译表" :icon="Columns3">
      <div class="editor-tools">
        <label class="field search-field">
          <span class="field-label"><Search class="field-label-icon" />搜索</span>
          <input id="tableSearch" v-model="search" placeholder="搜索原文、译文、场景或组件" @input="loadTranslations">
        </label>
        <div class="editor-actions">
          <div class="column-control">
            <button id="columnMenuButton" class="secondary" type="button" aria-controls="columnChooser" :aria-expanded="columnMenuOpen" @click="columnMenuOpen = !columnMenuOpen"><Columns3 class="button-icon" />列显示</button>
            <div class="column-chooser" id="columnChooser" :class="{ open: columnMenuOpen }">
              <div class="column-chooser-head">
                <span>列显示</span>
                <button id="showAllColumns" type="button" @click="showAllColumns">全部显示</button>
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
          <button id="clearTableFilters" class="secondary" type="button" :class="{ 'filter-active': hasColumnFilters }" @click="clearAllColumnFilters"><FilterX class="button-icon" />清空筛选</button>
          <input id="importFile" ref="importFile" class="hidden-file-input" type="file" accept=".json,.csv,text/csv,application/json" @change="importRows">
          <button id="importRows" class="secondary file-action-button" type="button" @click="openImportPicker"><Upload class="button-icon" />导入</button>
          <div class="export-control">
            <button id="exportRows" class="secondary file-action-button" type="button" aria-controls="exportMenu" :aria-expanded="exportMenuOpen" @click="exportMenuOpen = !exportMenuOpen"><Download class="button-icon" />导出</button>
            <div class="export-menu" id="exportMenu" :class="{ open: exportMenuOpen }" role="menu" aria-label="导出格式">
              <button type="button" role="menuitem" data-export-format="json" @click="exportRows('json')">JSON 文件</button>
              <button type="button" role="menuitem" data-export-format="csv" @click="exportRows('csv')">CSV 文件</button>
            </div>
          </div>
        </div>
      </div>

      <div class="table-wrap" id="tableWrap" tabindex="0" @contextmenu="showContextMenu" @keydown="handleTableKeydown">
        <table>
          <colgroup id="translationColgroup">
            <col v-for="column in visibleColumns" :key="column.key" :style="{ width: `${columnWidth(column)}px` }">
          </colgroup>
          <thead>
            <tr id="translationHead">
              <th v-for="column in visibleColumns" :key="column.key" :data-column-key="column.key" :aria-sort="ariaSort(column)" :data-sort-state="sortState(column)">
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
          <tbody id="translationBody">
            <tr v-for="(row, rowIndex) in rows" :key="rowKey(row)">
              <td
                v-for="column in visibleColumns"
                :key="column.key"
                :data-cell="cellKey(rowIndex, column.key)"
                :data-row-index="rowIndex"
                :data-column-key="column.key"
                :class="{ selected: isCellSelected(rowIndex, column), dirty: dirtyRows.has(rowKey(row)) && column.editable }"
                :title="cellValue(row, column.key)"
                @click="selectCell(rowIndex, column, $event)"
                @contextmenu.stop.prevent="openCellContextMenu($event, rowIndex, column)"
              >
                <textarea
                  v-if="column.editable"
                  class="cell-editor"
                  :value="displayCellValue(row, column)"
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
      <div class="message" id="tableMessage">
        共 {{ totalCount }} 行，当前显示 {{ rows.length }} 行。<span v-if="dirtyRows.size"> 待保存 {{ dirtyRows.size }} 行。</span> {{ tableMessage }}
      </div>
    </SectionPanel>

    <div
      class="context-menu"
      id="tableContextMenu"
      :class="{ open: contextMenu.open }"
      :style="{ left: `${contextMenu.x}px`, top: `${contextMenu.y}px` }"
      @click.stop
    >
      <button data-table-action="refresh" type="button" @click="handleTableAction('refresh')"><RefreshCw class="button-icon" />刷新表格</button>
      <button data-table-action="copy" type="button" @click="handleTableAction('copy')"><Copy class="button-icon" />复制选区</button>
      <button data-table-action="paste" type="button" @click="handleTableAction('paste')"><ClipboardPaste class="button-icon" />粘贴到选区</button>
      <button data-table-action="retranslate" type="button" @click="handleTableAction('retranslate')"><RefreshCw class="button-icon" />重翻选中行</button>
      <button data-table-action="highlight" type="button" @click="handleTableAction('highlight')"><MapPin class="button-icon" />高亮显示</button>
      <button data-table-action="delete" class="danger" type="button" @click="handleTableAction('delete')"><Trash2 class="button-icon" />删除选中已翻译文本</button>
      <button data-table-action="save" type="button" @click="handleTableAction('save')"><Save class="button-icon" />保存修改</button>
    </div>

    <div
      class="column-filter-menu"
      id="columnFilterMenu"
      :class="{ open: filterMenu.open }"
      :style="{ left: `${filterMenu.x}px`, top: `${filterMenu.y}px` }"
      role="dialog"
      aria-label="列筛选"
      @click.stop
    >
      <input id="columnFilterSearch" v-model="filterMenu.optionSearch" placeholder="搜索筛选值" @input="loadColumnFilterOptions(filterMenu.column, filterMenu.optionSearch)">
      <div class="filter-option-list">
        <label v-for="option in filterMenu.options" :key="filterValueKey(option.Value)" class="filter-option-row">
          <input type="checkbox" :checked="filterMenu.draft.includes(filterValueKey(option.Value))" @change="toggleFilterValue(filterValueKey(option.Value), ($event.target as HTMLInputElement).checked)">
          <span>{{ filterValueLabel(option.Value) }}</span>
          <small>{{ option.Count }}</small>
        </label>
      </div>
      <div class="actions inline-actions">
        <button id="clearColumnFilter" class="secondary" type="button" @click="applyColumnFilter(filterMenu.column, [])">清空</button>
        <button id="applyColumnFilter" type="button" @click="applyColumnFilter()">应用</button>
      </div>
    </div>
  </section>
</template>
