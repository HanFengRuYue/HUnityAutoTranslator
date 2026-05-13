<script setup lang="ts">
import { computed, onMounted, onUnmounted, reactive, ref, watch } from "vue";
import type { Component } from "vue";
import {
  ChevronLeft,
  ChevronRight,
  ChevronsUpDown,
  ClipboardPaste,
  Columns3,
  Copy,
  Database,
  Download,
  Filter,
  FilterX,
  Gamepad2,
  HardDrive,
  RefreshCw,
  Save,
  Search,
  SortAsc,
  SortDesc,
  Trash2,
  Upload
} from "lucide-vue-next";
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
} from "../table";
import { safeInvoke } from "../api/client";
import {
  requestNavigation,
  selectedGame as selectedGameFromStore,
  showToast,
  toolboxStore
} from "../state/toolboxStore";
import type {
  DatabaseMaintenanceResult,
  DeleteResult,
  TranslationCacheEntry,
  TranslationCacheFilterOption,
  TranslationCacheFilterOptionPage,
  TranslationCacheImportResult,
  TranslationCachePage
} from "../types/api";

interface CellAddress {
  row: number;
  columnKey: TableColumn["key"];
}

const selectedGame = computed(() => selectedGameFromStore());

const rows = ref<TranslationCacheEntry[]>([]);
const totalCount = ref(0);
const tableSearch = ref("");
const tableLoading = ref(false);
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
const tableMessage = ref("选择游戏后读取离线 SQLite 翻译缓存。");
const maintenanceResult = ref<DatabaseMaintenanceResult | null>(null);
const columnFilters = reactive<Record<string, string[]>>(loadColumnFilters());
const columnWidths = reactive<Record<string, number>>(loadColumnWidths());
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

function buildColumnFilterPayload(): Record<string, string[]> {
  return Object.fromEntries(Object.entries(columnFilters).filter(([, values]) => values.length > 0));
}

async function loadTranslations(): Promise<void> {
  if (!selectedGame.value) {
    rows.value = [];
    totalCount.value = 0;
    tableMessage.value = "请先在游戏库选择游戏。";
    return;
  }

  tableLoading.value = true;
  try {
    const page = await safeInvoke<TranslationCachePage>("queryTranslations", {
      gameRoot: selectedGame.value.Root,
      search: tableSearch.value,
      sort: sortColumn.value,
      direction: sortDirection.value,
      offset: 0,
      limit: 100,
      columnFilters: buildColumnFilterPayload()
    });
    if (!page) {
      return;
    }
    rows.value = page.Items;
    totalCount.value = page.TotalCount;
    clearSelection();
    tableMessage.value = `共 ${page.TotalCount} 行，当前显示 ${page.Items.length} 行。`;
  } finally {
    tableLoading.value = false;
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
  return Math.max(80, Math.min(720, width));
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

function clearSelection(): void {
  selectedCells.value = new Set();
  selectionAnchor.value = null;
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
    document.getElementById("toolboxTableWrap")?.focus({ preventScroll: true });
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
  for (let row = minRow; row <= maxRow; row += 1) {
    for (let columnIndex = minColumn; columnIndex <= maxColumn; columnIndex += 1) {
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

function clipboardLines(text: string): string[] {
  const lines = text.replace(/\r\n/g, "\n").replace(/\r/g, "\n").split("\n");
  if (lines.length > 1 && lines[lines.length - 1] === "") {
    lines.pop();
  }
  return lines;
}

async function copyCells(): Promise<void> {
  const cells = selectedCellAddresses();
  if (!cells.length) {
    tableMessage.value = "没有选中的单元格。";
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
}

async function pasteCells(): Promise<void> {
  const start = firstSelectedCell();
  if (!start) {
    tableMessage.value = "请先选择粘贴起点。";
    return;
  }

  const startColumnIndex = visibleColumns.value.findIndex((column) => column.key === start.columnKey);
  if (startColumnIndex < 0) {
    tableMessage.value = "请先选择可见列中的粘贴起点。";
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
}

function clearSelectedEditableCells(): void {
  const cells = selectedCellAddresses();
  let cleared = 0;
  for (const cell of cells) {
    const column = visibleColumns.value.find((item) => item.key === cell.columnKey);
    if (column?.editable && rows.value[cell.row]) {
      updateCellValue(cell.row, column, "");
      cleared += 1;
    }
  }

  tableMessage.value = cleared ? `已清空 ${cleared} 个单元格，等待保存。` : "没有选中的可编辑单元格。";
}

async function saveRows(): Promise<void> {
  if (!selectedGame.value || dirtyRows.size === 0) {
    return;
  }

  for (const row of dirtyRows.values()) {
    await safeInvoke<TranslationCacheEntry>("updateTranslation", {
      gameRoot: selectedGame.value.Root,
      entry: row
    }, { silent: true });
  }
  const savedCount = dirtyRows.size;
  dirtyRows.clear();
  await loadTranslations();
  tableMessage.value = `已保存 ${savedCount} 行修改。`;
  showToast(`已保存 ${savedCount} 行修改。`, "ok");
}

async function deleteSelectedRows(): Promise<void> {
  if (!selectedGame.value || !selectedRows.value.length) {
    tableMessage.value = "请先选择要删除的行。";
    return;
  }

  if (!confirm(`删除选中的 ${selectedRows.value.length} 行已翻译文本？`)) {
    return;
  }

  const result = await safeInvoke<DeleteResult>("deleteTranslations", {
    gameRoot: selectedGame.value.Root,
    entries: selectedRows.value
  });
  if (!result) {
    return;
  }
  dirtyRows.clear();
  clearSelection();
  await loadTranslations();
  tableMessage.value = `已删除 ${result.DeletedCount} 行。`;
  showToast(`已删除 ${result.DeletedCount} 行。`, "ok");
}

function openImportPicker(): void {
  if (importFile.value) {
    importFile.value.value = "";
  }
  importFile.value?.click();
}

async function importRows(): Promise<void> {
  const file = importFile.value?.files?.[0];
  if (!selectedGame.value || !file) {
    return;
  }

  const text = await file.text();
  const format = file.name.toLowerCase().endsWith(".csv") ? "csv" : "json";
  const result = await safeInvoke<TranslationCacheImportResult>("importTranslations", {
    gameRoot: selectedGame.value.Root,
    format,
    content: text
  });
  if (!result) {
    return;
  }
  await loadTranslations();
  tableMessage.value = `已导入 ${result.ImportedCount} 行。${result.Errors.length ? ` ${result.Errors.length} 个错误。` : ""}`;
  showToast(tableMessage.value, result.Errors.length ? "warn" : "ok");
  if (importFile.value) {
    importFile.value.value = "";
  }
}

function formatExportTimestamp(date = new Date()): string {
  const part = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}${part(date.getMonth() + 1)}${part(date.getDate())}-${part(date.getHours())}${part(date.getMinutes())}${part(date.getSeconds())}`;
}

function sanitizeFileNamePart(value: string | null | undefined): string {
  const cleaned = (value ?? "")
    .trim()
    .replace(/[<>:"/\\|?*\x00-\x1f]+/g, "-")
    .replace(/\s+/g, " ")
    .replace(/\.+$/g, "")
    .trim();
  return cleaned || "unknown-game";
}

async function exportRows(format: "json" | "csv"): Promise<void> {
  exportMenuOpen.value = false;
  if (!selectedGame.value) {
    tableMessage.value = "请先选择游戏。";
    return;
  }

  const text = await safeInvoke<string>("exportTranslations", {
    gameRoot: selectedGame.value.Root,
    format
  });
  if (text === null) {
    return;
  }
  const blob = new Blob([text], { type: format === "csv" ? "text/csv;charset=utf-8" : "application/json;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `hunity-translations-${sanitizeFileNamePart(selectedGame.value.Name)}-${formatExportTimestamp()}.${format}`;
  anchor.click();
  URL.revokeObjectURL(url);
  tableMessage.value = "导出已开始。";
  showToast(tableMessage.value, "ok");
}

async function runMaintenance(): Promise<void> {
  if (!selectedGame.value) {
    tableMessage.value = "请先选择游戏。";
    return;
  }

  const result = await safeInvoke<DatabaseMaintenanceResult>("runDatabaseMaintenance", {
    gameRoot: selectedGame.value.Root,
    createBackup: true,
    runIntegrityCheck: true,
    reindex: true,
    vacuum: false
  });
  if (!result) {
    return;
  }
  maintenanceResult.value = result;
  tableMessage.value = `维护完成：${result.Actions.join("、")}`;
  showToast(tableMessage.value, "ok");
}

function clearAllColumnFilters(): void {
  for (const key of Object.keys(columnFilters)) {
    delete columnFilters[key];
  }
  persistColumnFilters(columnFilters);
  hideColumnFilterMenu();
  void loadTranslations();
}

async function loadColumnFilterOptions(column: string, optionSearch = ""): Promise<void> {
  if (!selectedGame.value) {
    return;
  }

  const page = await safeInvoke<TranslationCacheFilterOptionPage>("getTranslationFilterOptions", {
    gameRoot: selectedGame.value.Root,
    column,
    search: tableSearch.value,
    optionSearch,
    limit: 80,
    columnFilters: buildColumnFilterPayload()
  }, { silent: true });
  if (page) {
    filterMenu.options = page.Items;
  }
}

function positionFilterMenu(anchor: HTMLElement): void {
  const rect = anchor.getBoundingClientRect();
  const margin = 12;
  const menuWidth = Math.min(320, window.innerWidth - margin * 2);
  const menuHeight = Math.min(360, window.innerHeight - margin * 2);
  filterMenu.x = Math.max(margin, Math.min(rect.right - menuWidth, window.innerWidth - menuWidth - margin));
  filterMenu.y = rect.bottom + 8 + menuHeight <= window.innerHeight - margin
    ? Math.max(margin, rect.bottom + 8)
    : Math.max(margin, rect.top - menuHeight - 8);
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

function filterValueKey(value: string | null): string {
  return value ?? emptyFilterValue;
}

function filterValueLabel(value: string | null): string {
  return value && value.length ? value : "(空)";
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
    clearSelectedEditableCells();
  }
}

function formatDateTime(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }

  return date.toLocaleString("zh-CN", { hour12: false });
}

function handleDocumentClick(event: MouseEvent): void {
  const target = event.target as HTMLElement | null;
  if (!target?.closest("#toolboxColumnFilterMenu") && !target?.closest(".header-filter")) {
    hideColumnFilterMenu();
  }
}

watch(() => toolboxStore.selectedGameId, (id) => {
  if (id) {
    void loadTranslations();
  } else {
    rows.value = [];
    totalCount.value = 0;
    tableMessage.value = "请先在游戏库选择游戏。";
  }
});

onMounted(() => {
  if (selectedGame.value) {
    void loadTranslations();
  }
  document.addEventListener("click", handleDocumentClick);
});

onUnmounted(() => {
  document.removeEventListener("click", handleDocumentClick);
});
</script>

<template>
  <section class="page translations-page">
    <header class="page-hero">
      <div>
        <span class="eyebrow">{{ selectedGame ? selectedGame.Name : "未选择游戏" }}</span>
        <h1>译文编辑</h1>
        <p>{{ selectedGame ? `${selectedGame.Root}\\BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite` : "选择游戏后离线读取 SQLite 翻译缓存。" }}</p>
      </div>
      <div class="status-strip">
        <span class="pill">{{ tableLoading ? "加载中" : `共 ${totalCount} 行` }}</span>
        <span v-if="dirtyRows.size" class="pill warn">待保存 {{ dirtyRows.size }} 行</span>
      </div>
    </header>

    <div v-if="!selectedGame" class="panel empty-state">
      <Database class="empty-icon" />
      <strong>没有译文数据库目标</strong>
      <span>译文编辑会离线读取所选游戏目录里的 translation-cache.sqlite。</span>
      <button class="button-primary" type="button" @click="requestNavigation('library')">
        <Gamepad2 class="icon" />打开游戏库
      </button>
    </div>

    <template v-else>
      <div class="editor-tools panel">
        <label class="field search-field">
          <span><Search class="field-icon" />搜索</span>
          <input v-model="tableSearch" placeholder="搜索原文、译文、场景或组件" @input="loadTranslations">
        </label>
        <div class="editor-actions">
          <button type="button" :disabled="tableLoading" @click="loadTranslations"><RefreshCw class="icon" />{{ tableLoading ? "刷新中" : "刷新" }}</button>
          <button class="button-primary" type="button" :disabled="dirtyRows.size === 0" @click="saveRows"><Save class="icon" />保存修改</button>
          <button type="button" @click="copyCells"><Copy class="icon" />复制</button>
          <button type="button" @click="pasteCells"><ClipboardPaste class="icon" />粘贴</button>
          <button class="button-danger" type="button" @click="deleteSelectedRows"><Trash2 class="icon" />删除</button>
          <button type="button" @click="runMaintenance"><HardDrive class="icon" />重建索引</button>
          <div class="column-control">
            <button type="button" :aria-expanded="columnMenuOpen" @click="columnMenuOpen = !columnMenuOpen"><Columns3 class="icon" />列显示</button>
            <div class="column-chooser" :class="{ open: columnMenuOpen }">
              <div class="column-chooser-head">
                <span>列显示</span>
                <button type="button" @click="showAllColumns">全部显示</button>
              </div>
              <div v-for="column in orderedColumns" :key="column.key" class="column-choice">
                <label class="check">
                  <input type="checkbox" :checked="visibleKeys.includes(column.key)" @change="toggleColumn(column.key, ($event.target as HTMLInputElement).checked)">
                  <span>{{ column.label }}</span>
                </label>
                <span class="column-move-buttons">
                  <button type="button" @click="moveColumn(column.key, -1)"><ChevronLeft class="icon" /></button>
                  <button type="button" @click="moveColumn(column.key, 1)"><ChevronRight class="icon" /></button>
                </span>
              </div>
            </div>
          </div>
          <button type="button" :class="{ 'filter-active': hasColumnFilters }" @click="clearAllColumnFilters"><FilterX class="icon" />清空筛选</button>
          <input ref="importFile" class="hidden-file-input" type="file" accept=".json,.csv,text/csv,application/json" @change="importRows">
          <button type="button" @click="openImportPicker"><Upload class="icon" />导入</button>
          <div class="export-control">
            <button type="button" :aria-expanded="exportMenuOpen" @click="exportMenuOpen = !exportMenuOpen"><Download class="icon" />导出</button>
            <div class="export-menu" :class="{ open: exportMenuOpen }">
              <button type="button" @click="exportRows('json')">JSON 文件</button>
              <button type="button" @click="exportRows('csv')">CSV 文件</button>
            </div>
          </div>
        </div>
      </div>

      <div class="translation-table-wrap" id="toolboxTableWrap" tabindex="0" @keydown="handleTableKeydown">
        <table class="translation-table">
          <colgroup>
            <col v-for="column in visibleColumns" :key="column.key" :style="{ width: `${columnWidth(column)}px` }">
          </colgroup>
          <thead>
            <tr>
              <th v-for="column in visibleColumns" :key="column.key" :aria-sort="sortState(column)">
                <div class="header-inner">
                  <button class="header-title" type="button" :title="`按 ${column.label} 排序`" @click="setSort(column)">
                    <span>{{ column.label }}</span>
                    <component :is="sortIcon(column)" class="sort-icon" />
                  </button>
                  <button class="header-filter" type="button" :class="{ 'filter-active': hasColumnFilter(column) }" :title="`筛选 ${column.label}`" @click.stop="openColumnFilterMenu(column, $event)">
                    <Filter class="table-icon" />
                  </button>
                </div>
                <span class="col-resizer" @pointerdown.stop.prevent="startColumnResize($event, column)"></span>
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="(row, rowIndex) in rows" :key="rowKey(row)">
              <td
                v-for="column in visibleColumns"
                :key="column.key"
                :class="{ selected: isCellSelected(rowIndex, column), dirty: dirtyRows.has(rowKey(row)) && column.editable }"
                :title="cellValue(row, column.key)"
                @click="selectCell(rowIndex, column, $event)"
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
            <tr v-if="!rows.length">
              <td :colspan="visibleColumns.length || 1" class="empty-row">{{ tableLoading ? "正在读取..." : "没有可显示的译文。" }}</td>
            </tr>
          </tbody>
        </table>
      </div>
      <p class="message">
        {{ tableMessage }}<span v-if="dirtyRows.size"> 待保存 {{ dirtyRows.size }} 行。</span>
        <span v-if="maintenanceResult?.BackupPath"> 备份：{{ maintenanceResult.BackupPath }}</span>
      </p>

      <div
        class="column-filter-menu"
        id="toolboxColumnFilterMenu"
        :class="{ open: filterMenu.open }"
        :style="{ left: `${filterMenu.x}px`, top: `${filterMenu.y}px` }"
        role="dialog"
        aria-label="列筛选"
        @click.stop
      >
        <input v-model="filterMenu.optionSearch" placeholder="搜索筛选值" @input="loadColumnFilterOptions(filterMenu.column, filterMenu.optionSearch)">
        <div class="filter-option-list">
          <label v-for="option in filterMenu.options" :key="filterValueKey(option.Value)" class="filter-option-row">
            <input type="checkbox" :checked="filterMenu.draft.includes(filterValueKey(option.Value))" @change="toggleFilterValue(filterValueKey(option.Value), ($event.target as HTMLInputElement).checked)">
            <span>{{ filterValueLabel(option.Value) }}</span>
            <small>{{ option.Count }}</small>
          </label>
        </div>
        <div class="actions inline-actions">
          <button type="button" @click="applyColumnFilter(filterMenu.column, [])">清空</button>
          <button class="button-primary" type="button" @click="applyColumnFilter()">应用</button>
        </div>
      </div>
    </template>
  </section>
</template>
