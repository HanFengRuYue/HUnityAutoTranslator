import type { TranslationCacheEntry } from "../types/api";

export interface TableColumn {
  key: keyof TranslationCacheEntry & string;
  label: string;
  sort: string;
  editable: boolean;
  width: number;
}

export const emptyFilterValue = "__HUNITY_EMPTY__";
export const visibleColumnStorageKey = "hunity.editor.visibleColumns";
export const columnOrderStorageKey = "hunity.editor.columnOrder";
export const columnFilterStorageKey = "hunity.editor.columnFilters";

export const defaultColumns: TableColumn[] = [
  { key: "SourceText", label: "原文", sort: "source_text", editable: false, width: 280 },
  { key: "TranslatedText", label: "译文", sort: "translated_text", editable: true, width: 280 },
  { key: "TargetLanguage", label: "目标语言", sort: "target_language", editable: false, width: 110 },
  { key: "SceneName", label: "场景", sort: "scene_name", editable: true, width: 150 },
  { key: "ComponentHierarchy", label: "组件层级", sort: "component_hierarchy", editable: true, width: 240 },
  { key: "ComponentType", label: "组件", sort: "component_type", editable: true, width: 150 },
  { key: "ReplacementFont", label: "替换字体", sort: "replacement_font", editable: true, width: 180 },
  { key: "ProviderKind", label: "服务商", sort: "provider_kind", editable: false, width: 110 },
  { key: "ProviderModel", label: "模型", sort: "provider_model", editable: false, width: 160 },
  { key: "CreatedUtc", label: "创建时间", sort: "created_utc", editable: false, width: 170 },
  { key: "UpdatedUtc", label: "更新时间", sort: "updated_utc", editable: false, width: 170 }
];

function readStringArray(key: string, fallback: string[]): string[] {
  try {
    const parsed = JSON.parse(localStorage.getItem(key) ?? "null");
    return Array.isArray(parsed) ? parsed.filter((value) => typeof value === "string") : fallback;
  } catch {
    localStorage.removeItem(key);
    return fallback;
  }
}

export function loadVisibleColumns(): string[] {
  return readStringArray(visibleColumnStorageKey, defaultColumns.map((column) => column.key));
}

export function saveVisibleColumns(keys: string[]): void {
  localStorage.setItem(visibleColumnStorageKey, JSON.stringify(keys));
}

export function loadColumnOrder(): string[] {
  return readStringArray(columnOrderStorageKey, defaultColumns.map((column) => column.key));
}

export function saveColumnOrder(keys: string[]): void {
  localStorage.setItem(columnOrderStorageKey, JSON.stringify(keys));
}

export function loadColumnFilters(): Record<string, string[]> {
  try {
    const parsed = JSON.parse(localStorage.getItem(columnFilterStorageKey) ?? "{}");
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? Object.fromEntries(Object.entries(parsed).filter(([, value]) => Array.isArray(value))) as Record<string, string[]>
      : {};
  } catch {
    localStorage.removeItem(columnFilterStorageKey);
    return {};
  }
}

export function persistColumnFilters(filters: Record<string, string[]>): void {
  localStorage.setItem(columnFilterStorageKey, JSON.stringify(filters));
}

export function rowKey(row: TranslationCacheEntry): string {
  return [
    row.SourceText,
    row.TargetLanguage,
    row.SceneName ?? "",
    row.ComponentHierarchy ?? "",
    row.ProviderKind,
    row.ProviderBaseUrl,
    row.ProviderEndpoint,
    row.ProviderModel,
    row.PromptPolicyVersion
  ].join("\u001f");
}

export function cellValue(row: TranslationCacheEntry, key: keyof TranslationCacheEntry & string): string {
  return String(row[key] ?? "");
}

export function toTsv(rows: TranslationCacheEntry[], columns: TableColumn[]): string {
  return rows
    .map((row) => columns.map((column) => cellValue(row, column.key).replace(/\r?\n/g, " ")).join("\t"))
    .join("\n");
}
