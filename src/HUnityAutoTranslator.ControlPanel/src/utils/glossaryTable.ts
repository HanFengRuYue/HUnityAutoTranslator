import type { GlossaryTerm } from "../types/api";

export interface GlossaryTableColumn {
  key: keyof GlossaryTerm & string;
  label: string;
  sort: string;
  editable: boolean;
  width: number;
  align?: "center";
  editor?: "checkbox" | "text" | "language";
}

export const emptyFilterValue = "__HUNITY_EMPTY__";
export const visibleColumnStorageKey = "hunity.glossary.visibleColumns";
export const columnOrderStorageKey = "hunity.glossary.columnOrder";
export const columnFilterStorageKey = "hunity.glossary.columnFilters";
export const columnWidthStorageKey = "hunity.glossary.columnWidths";

export const defaultGlossaryColumns: GlossaryTableColumn[] = [
  { key: "Enabled", label: "启用", sort: "enabled", editable: true, width: 74, align: "center", editor: "checkbox" },
  { key: "SourceTerm", label: "原术语", sort: "source_term", editable: true, width: 220, editor: "text" },
  { key: "TargetTerm", label: "指定译名", sort: "target_term", editable: true, width: 220, editor: "text" },
  { key: "TargetLanguage", label: "目标语言", sort: "target_language", editable: true, width: 150, editor: "language" },
  { key: "Note", label: "备注", sort: "note", editable: true, width: 190, editor: "text" },
  { key: "Source", label: "来源", sort: "source", editable: false, width: 86, align: "center" },
  { key: "UsageCount", label: "使用", sort: "usage_count", editable: false, width: 86, align: "center" },
  { key: "CreatedUtc", label: "创建", sort: "created_utc", editable: false, width: 180 },
  { key: "UpdatedUtc", label: "更新", sort: "updated_utc", editable: false, width: 180, align: "center" }
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
  return Object.fromEntries(defaultGlossaryColumns.map((column) => [column.key, column.width]));
}

export function loadGlossaryVisibleColumns(): string[] {
  return readStringArray(visibleColumnStorageKey, defaultGlossaryColumns.map((column) => column.key));
}

export function saveGlossaryVisibleColumns(keys: string[]): void {
  localStorage.setItem(visibleColumnStorageKey, JSON.stringify(keys));
}

export function loadGlossaryColumnOrder(): string[] {
  return readStringArray(columnOrderStorageKey, defaultGlossaryColumns.map((column) => column.key));
}

export function saveGlossaryColumnOrder(keys: string[]): void {
  localStorage.setItem(columnOrderStorageKey, JSON.stringify(keys));
}

export function loadGlossaryColumnWidths(): Record<string, number> {
  const fallback = defaultColumnWidths();
  try {
    const parsed = JSON.parse(localStorage.getItem(columnWidthStorageKey) ?? "{}");
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      return fallback;
    }

    const knownKeys = new Set<string>(defaultGlossaryColumns.map((column) => column.key));
    const widths = { ...fallback };
    for (const [key, value] of Object.entries(parsed)) {
      if (knownKeys.has(key) && typeof value === "number" && Number.isFinite(value) && value > 0) {
        widths[key] = value;
      }
    }
    return widths;
  } catch {
    localStorage.removeItem(columnWidthStorageKey);
    return fallback;
  }
}

export function saveGlossaryColumnWidths(widths: Record<string, number>): void {
  const knownKeys = new Set<string>(defaultGlossaryColumns.map((column) => column.key));
  const clean = Object.fromEntries(
    Object.entries(widths).filter(([key, value]) => knownKeys.has(key) && Number.isFinite(value) && value > 0)
  );
  localStorage.setItem(columnWidthStorageKey, JSON.stringify(clean));
}

export function loadGlossaryColumnFilters(): Record<string, string[]> {
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

export function persistGlossaryColumnFilters(filters: Record<string, string[]>): void {
  localStorage.setItem(columnFilterStorageKey, JSON.stringify(filters));
}

export function glossaryRowKey(row: GlossaryTerm): string {
  return [row.TargetLanguage, row.NormalizedSourceTerm].join("\u001f");
}

export function glossaryCellValue(row: GlossaryTerm, key: keyof GlossaryTerm & string): string {
  if (key === "Enabled") {
    return row.Enabled ? "true" : "false";
  }

  return String(row[key] ?? "");
}
