import type { TranslationCacheEntry } from "./types";

export interface TableColumn {
  key: keyof TranslationCacheEntry & string;
  label: string;
  sort: string;
  editable: boolean;
  width: number;
}

export const emptyFilterValue = "__HUNITY_EMPTY__";
export const visibleColumnStorageKey = "hunity.toolbox.editor.visibleColumns";
export const columnOrderStorageKey = "hunity.toolbox.editor.columnOrder";
export const columnFilterStorageKey = "hunity.toolbox.editor.columnFilters";
export const columnWidthStorageKey = "hunity.toolbox.editor.columnWidths";

export const defaultColumns: TableColumn[] = [
  { key: "SourceText", label: "原文", sort: "source_text", editable: false, width: 300 },
  { key: "TranslatedText", label: "译文", sort: "translated_text", editable: true, width: 300 },
  { key: "TargetLanguage", label: "目标语言", sort: "target_language", editable: false, width: 110 },
  { key: "SceneName", label: "场景", sort: "scene_name", editable: true, width: 150 },
  { key: "ComponentHierarchy", label: "组件层级", sort: "component_hierarchy", editable: true, width: 250 },
  { key: "ComponentType", label: "组件类型", sort: "component_type", editable: true, width: 150 },
  { key: "ReplacementFont", label: "替换字体", sort: "replacement_font", editable: true, width: 190 },
  { key: "ProviderKind", label: "服务商", sort: "provider_kind", editable: false, width: 110 },
  { key: "ProviderModel", label: "模型", sort: "provider_model", editable: false, width: 160 },
  { key: "CreatedUtc", label: "创建时间", sort: "created_utc", editable: false, width: 190 },
  { key: "UpdatedUtc", label: "更新时间", sort: "updated_utc", editable: false, width: 190 }
];

function readStoredValue(storageKey: string): string | null {
  try {
    return window.localStorage.getItem(storageKey);
  } catch {
    return null;
  }
}

function writeStoredValue(storageKey: string, value: string): void {
  try {
    window.localStorage.setItem(storageKey, value);
  } catch {
    // WebView2 NavigateToString can reject storage access before Vue mounts.
  }
}

function removeStoredValue(storageKey: string): void {
  try {
    window.localStorage.removeItem(storageKey);
  } catch {
    // WebView2 NavigateToString can reject storage access before Vue mounts.
  }
}

function readStringArray(key: string, fallback: string[]): string[] {
  try {
    const parsed = JSON.parse(readStoredValue(key) ?? "null");
    return Array.isArray(parsed) ? parsed.filter((value) => typeof value === "string") : fallback;
  } catch {
    removeStoredValue(key);
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
  writeStoredValue(visibleColumnStorageKey, JSON.stringify(keys));
}

export function loadColumnOrder(): string[] {
  return readStringArray(columnOrderStorageKey, defaultColumns.map((column) => column.key));
}

export function saveColumnOrder(keys: string[]): void {
  writeStoredValue(columnOrderStorageKey, JSON.stringify(keys));
}

export function loadColumnWidths(): Record<string, number> {
  const fallback = defaultColumnWidths();
  try {
    const parsed = JSON.parse(readStoredValue(columnWidthStorageKey) ?? "{}");
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return fallback;
    }

    const knownKeys = new Set<string>(defaultColumns.map((column) => column.key));
    const widths = { ...fallback };
    for (const [key, value] of Object.entries(parsed)) {
      if (knownKeys.has(key) && typeof value === "number" && Number.isFinite(value) && value > 0) {
        widths[key] = value;
      }
    }
    return widths;
  } catch {
    removeStoredValue(columnWidthStorageKey);
    return fallback;
  }
}

export function saveColumnWidths(widths: Record<string, number>): void {
  const knownKeys = new Set<string>(defaultColumns.map((column) => column.key));
  const clean = Object.fromEntries(
    Object.entries(widths).filter(([key, value]) => knownKeys.has(key) && Number.isFinite(value) && value > 0)
  );
  writeStoredValue(columnWidthStorageKey, JSON.stringify(clean));
}

export function loadColumnFilters(): Record<string, string[]> {
  try {
    const parsed = JSON.parse(readStoredValue(columnFilterStorageKey) ?? "{}");
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? Object.fromEntries(Object.entries(parsed).filter(([, value]) => Array.isArray(value))) as Record<string, string[]>
      : {};
  } catch {
    removeStoredValue(columnFilterStorageKey);
    return {};
  }
}

export function persistColumnFilters(filters: Record<string, string[]>): void {
  writeStoredValue(columnFilterStorageKey, JSON.stringify(filters));
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
