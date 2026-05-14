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
export const columnWidthStorageKey = "hunity.editor.columnWidths";
export const minColumnWidth = 96;

export const defaultColumns: TableColumn[] = [
  { key: "SourceText", label: "原文", sort: "source_text", editable: false, width: 280 },
  { key: "TranslatedText", label: "译文", sort: "translated_text", editable: true, width: 280 },
  { key: "TargetLanguage", label: "目标语言", sort: "target_language", editable: false, width: 145 },
  { key: "SceneName", label: "场景", sort: "scene_name", editable: true, width: 150 },
  { key: "ComponentHierarchy", label: "组件层级", sort: "component_hierarchy", editable: true, width: 240 },
  { key: "ComponentType", label: "组件", sort: "component_type", editable: true, width: 150 },
  { key: "ReplacementFont", label: "替换字体", sort: "replacement_font", editable: true, width: 180 },
  { key: "ProviderKind", label: "服务商", sort: "provider_kind", editable: false, width: 140 },
  { key: "ProviderModel", label: "模型", sort: "provider_model", editable: false, width: 175 },
  { key: "CreatedUtc", label: "创建时间", sort: "created_utc", editable: false, width: 190 },
  { key: "UpdatedUtc", label: "更新时间", sort: "updated_utc", editable: false, width: 190 }
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

function defaultColumnWidths(): Record<string, number> {
  return Object.fromEntries(defaultColumns.map((column) => [column.key, column.width]));
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

export function loadColumnWidths(): Record<string, number> {
  const fallback = defaultColumnWidths();
  try {
    const parsed = JSON.parse(localStorage.getItem(columnWidthStorageKey) ?? "{}");
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return fallback;
    }

    const knownKeys = new Set<string>(defaultColumns.map((column) => column.key));
    const widths = { ...fallback };
    for (const [key, value] of Object.entries(parsed)) {
      if (knownKeys.has(key) && typeof value === "number" && Number.isFinite(value) && value > 0) {
        widths[key] = Math.max(minColumnWidth, value);
      }
    }
    return widths;
  } catch {
    localStorage.removeItem(columnWidthStorageKey);
    return fallback;
  }
}

export function saveColumnWidths(widths: Record<string, number>): void {
  const knownKeys = new Set<string>(defaultColumns.map((column) => column.key));
  const clean = Object.fromEntries(
    Object.entries(widths).filter(([key, value]) => knownKeys.has(key) && Number.isFinite(value) && value > 0)
  );
  localStorage.setItem(columnWidthStorageKey, JSON.stringify(clean));
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
