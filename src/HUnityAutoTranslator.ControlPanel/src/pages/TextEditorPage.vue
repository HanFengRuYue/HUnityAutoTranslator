<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
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
  columnFilterStorageKey,
  defaultColumns,
  emptyFilterValue,
  loadColumnFilters,
  loadColumnOrder,
  loadVisibleColumns,
  persistColumnFilters,
  rowKey,
  saveColumnOrder,
  saveVisibleColumns,
  toTsv,
  type TableColumn
} from "../utils/table";

const rows = ref<TranslationCacheEntry[]>([]);
const totalCount = ref(0);
const search = ref("");
const loading = ref(false);
const sortColumn = ref("updated_utc");
const sortDirection = ref<"asc" | "desc">("desc");
const visibleKeys = ref(loadVisibleColumns());
const orderKeys = ref(loadColumnOrder());
const selectedKeys = ref(new Set<string>());
const dirtyRows = reactive(new Map<string, TranslationCacheEntry>());
const columnMenuOpen = ref(false);
const exportMenuOpen = ref(false);
const importFile = ref<HTMLInputElement | null>(null);
const tableMessage = ref("");
const columnFilters = reactive<Record<string, string[]>>(loadColumnFilters());
const filterMenu = reactive({
  open: false,
  column: "",
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
const selectedRows = computed(() => rows.value.filter((row) => selectedKeys.value.has(rowKey(row))));
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
    const page = await getJson<TranslationCachePage>(`/api/translations?${params.toString()}`);
    rows.value = page.Items;
    totalCount.value = page.TotalCount;
    selectedKeys.value = new Set([...selectedKeys.value].filter((key) => rows.value.some((row) => rowKey(row) === key)));
  } catch (error) {
    showToast(error instanceof Error ? error.message : "翻译表加载失败", "error");
  } finally {
    loading.value = false;
  }
}

function toggleColumn(key: string, checked: boolean): void {
  visibleKeys.value = checked ? Array.from(new Set([...visibleKeys.value, key])) : visibleKeys.value.filter((item) => item !== key);
  saveVisibleColumns(visibleKeys.value);
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

function toggleRow(row: TranslationCacheEntry, checked: boolean): void {
  const next = new Set(selectedKeys.value);
  const key = rowKey(row);
  if (checked) {
    next.add(key);
  } else {
    next.delete(key);
  }
  selectedKeys.value = next;
}

function updateCell(row: TranslationCacheEntry, column: TableColumn, value: string): void {
  (row as unknown as Record<string, string | null>)[column.key] = value;
  dirtyRows.set(rowKey(row), { ...row });
}

async function copySelected(): Promise<void> {
  const rowsToCopy = selectedRows.value.length ? selectedRows.value : rows.value;
  await navigator.clipboard.writeText(toTsv(rowsToCopy, visibleColumns.value));
  showToast("已复制表格内容。", "ok");
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
    showToast("请先选择一行。", "warn");
    return;
  }

  const result = await postJson<TranslationHighlightResult>("/api/translations/highlight", row);
  tableMessage.value = result.Message || "已发送高亮请求。";
  showToast(tableMessage.value, result.Status === "queued" ? "ok" : "warn");
}

async function deleteSelectedRows(): Promise<void> {
  if (!selectedRows.value.length) {
    showToast("请先选择要删除的行。", "warn");
    return;
  }

  if (!confirm(`删除选中的 ${selectedRows.value.length} 行已翻译文本？`)) {
    return;
  }

  const result = await deleteJson<DeleteResult>("/api/translations", selectedRows.value);
  selectedKeys.value = new Set();
  await loadTranslations();
  tableMessage.value = `已删除 ${result.DeletedCount} 行。`;
  showToast("选中行已删除。", "ok");
}

function openImportPicker(): void {
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

async function openColumnFilterMenu(column: TableColumn): Promise<void> {
  filterMenu.open = true;
  filterMenu.column = column.sort;
  filterMenu.optionSearch = "";
  filterMenu.draft = [...(columnFilters[column.sort] ?? [])];
  await loadColumnFilterOptions(column.sort);
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
  filterMenu.open = false;
  void loadTranslations();
}

function clearAllColumnFilters(): void {
  for (const key of Object.keys(columnFilters)) {
    delete columnFilters[key];
  }
  persistColumnFilters(columnFilters);
  filterMenu.open = false;
  void loadTranslations();
}

function filterValueKey(value: string | null): string {
  return value ?? "";
}

function filterValueLabel(value: string | null): string {
  return value && value.length ? value : "(空)";
}

onMounted(loadTranslations);
</script>

<template>
  <section class="page active" id="page-editor">
    <div class="page-head">
      <div>
        <h1>文本编辑</h1>
        <p>查看、编辑和批量处理本地 SQLite 翻译缓存。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="loading" @click="loadTranslations">{{ loading ? "刷新中" : "刷新" }}</button>
        <button class="primary" id="saveRows" type="button" :disabled="dirtyRows.size === 0" @click="saveRows">保存修改</button>
      </div>
    </div>

    <SectionPanel title="翻译表">
      <div class="editor-tools">
        <input id="tableSearch" v-model="search" placeholder="搜索原文、译文、场景或组件" @input="loadTranslations">
        <div class="editor-actions">
          <div class="column-control">
            <button id="columnMenuButton" class="secondary" type="button" aria-controls="columnChooser" :aria-expanded="columnMenuOpen" @click="columnMenuOpen = !columnMenuOpen">列显示</button>
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
          <button id="clearTableFilters" class="secondary" type="button" :class="{ 'filter-active': hasColumnFilters }" @click="clearAllColumnFilters">清空筛选</button>
          <button class="secondary" type="button" @click="copySelected">复制</button>
          <button class="secondary" type="button" data-table-action="retranslate" @click="retranslateSelectedRows">重翻选中行</button>
          <button class="secondary" type="button" data-table-action="highlight" @click="highlightSelectedRow">高亮显示</button>
          <button class="danger" type="button" data-table-action="delete" @click="deleteSelectedRows">删除</button>
          <input id="importFile" ref="importFile" class="hidden-file-input" type="file" accept=".json,.csv,text/csv,application/json" @change="importRows">
          <button id="importRows" class="secondary file-action-button" type="button" @click="openImportPicker">导入</button>
          <div class="export-control">
            <button id="exportRows" class="secondary file-action-button" type="button" aria-controls="exportMenu" :aria-expanded="exportMenuOpen" @click="exportMenuOpen = !exportMenuOpen">导出</button>
            <div class="export-menu" id="exportMenu" :class="{ open: exportMenuOpen }" role="menu" aria-label="导出格式">
              <button type="button" role="menuitem" data-export-format="json" @click="exportRows('json')">JSON 文件</button>
              <button type="button" role="menuitem" data-export-format="csv" @click="exportRows('csv')">CSV 文件</button>
            </div>
          </div>
        </div>
      </div>

      <div class="table-wrap" id="tableWrap" tabindex="0">
        <table>
          <colgroup id="translationColgroup">
            <col style="width:42px">
            <col v-for="column in visibleColumns" :key="column.key" :style="{ width: `${column.width}px` }">
          </colgroup>
          <thead>
            <tr id="translationHead">
              <th></th>
              <th v-for="column in visibleColumns" :key="column.key">
                <div class="header-inner">
                  <button class="header-title" type="button" @click="setSort(column)">{{ column.label }}</button>
                  <button class="header-filter" type="button" :class="{ 'filter-active': columnFilters[column.sort]?.length }" :data-filter-column="column.sort" @click.stop="openColumnFilterMenu(column)">筛</button>
                </div>
              </th>
            </tr>
          </thead>
          <tbody id="translationBody">
            <tr v-for="row in rows" :key="rowKey(row)" :class="{ selected: selectedKeys.has(rowKey(row)), dirty: dirtyRows.has(rowKey(row)) }">
              <td><input type="checkbox" :checked="selectedKeys.has(rowKey(row))" @change="toggleRow(row, ($event.target as HTMLInputElement).checked)"></td>
              <td v-for="column in visibleColumns" :key="column.key">
                <textarea
                  v-if="column.editable"
                  class="cell-editor"
                  :value="cellValue(row, column.key)"
                  @input="updateCell(row, column, ($event.target as HTMLTextAreaElement).value)"
                />
                <span v-else class="cell-text">{{ column.key.endsWith("Utc") ? formatDateTime(cellValue(row, column.key)) : cellValue(row, column.key) }}</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
      <div class="message" id="tableMessage">
        共 {{ totalCount }} 行，当前显示 {{ rows.length }} 行。<span v-if="dirtyRows.size"> 待保存 {{ dirtyRows.size }} 行。</span> {{ tableMessage }}
      </div>
    </SectionPanel>

    <div class="column-filter-menu" id="columnFilterMenu" :class="{ open: filterMenu.open }" role="dialog" aria-label="列筛选">
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
