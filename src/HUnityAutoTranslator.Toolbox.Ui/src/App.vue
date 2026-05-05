<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import type { Component } from "vue";
import {
  AlertTriangle,
  ArchiveRestore,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  ChevronsUpDown,
  ClipboardPaste,
  Columns3,
  Copy,
  Database,
  Download,
  FileText,
  Filter,
  FilterX,
  FolderOpen,
  FolderPlus,
  Gamepad2,
  Grid2X2,
  HardDrive,
  Info,
  Keyboard,
  Languages,
  List,
  Maximize2,
  Minus,
  MonitorCog,
  Moon,
  PackageCheck,
  Palette,
  PanelLeftClose,
  PanelLeftOpen,
  RefreshCw,
  RotateCcw,
  Save,
  ScanLine,
  Search,
  Settings,
  ShieldCheck,
  SlidersHorizontal,
  SortAsc,
  SortDesc,
  Square,
  Sun,
  Timer,
  Trash2,
  Type,
  Upload,
  Wand2,
  X
} from "lucide-vue-next";
import brandIcon from "../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import { invokeToolbox } from "./bridge";
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
} from "./table";
import type {
  DatabaseMaintenanceResult,
  DeleteResult,
  FontPickResult,
  GameInspection,
  GameLibraryEntry,
  InstallMode,
  InstallPlan,
  LibraryAccent,
  LibraryLayout,
  LibraryPosterSize,
  PageKey,
  PluginConfigPayload,
  PromptTemplateConfig,
  TextureImageTranslationConfig,
  ThemeMode,
  TranslationCacheEntry,
  TranslationCacheFilterOption,
  TranslationCacheFilterOptionPage,
  TranslationCacheImportResult,
  TranslationCachePage,
  TranslationQualityConfig,
  UpdateConfigRequest
} from "./types";

interface CellAddress {
  row: number;
  columnKey: TableColumn["key"];
}

type HotkeyField = "OpenControlPanelHotkey" | "ToggleTranslationHotkey" | "ForceScanHotkey" | "ToggleFontHotkey";

const themeStorageKey = "hunity.toolbox.theme";
const sidebarStorageKey = "hunity.toolbox.sidebarCollapsed";
const gameLibraryStorageKey = "hunity.toolbox.gameLibrary";
const selectedGameStorageKey = "hunity.toolbox.selectedGame";
const libraryLayoutStorageKey = "hunity.toolbox.libraryLayout";
const libraryPosterSizeStorageKey = "hunity.toolbox.libraryPosterSize";
const libraryAccentStorageKey = "hunity.toolbox.libraryAccent";
const hotkeyListeningText = "请按组合键...";

const promptTemplateDefaults: Record<keyof PromptTemplateConfig, string> = {
  SystemPrompt: "",
  GlossarySystemPolicy: "",
  BatchUserPrompt: "",
  GlossaryTermsSection: "",
  CurrentItemContextSection: "",
  ItemHintsSection: "",
  ContextExamplesSection: "",
  GlossaryRepairPrompt: "",
  QualityRepairPrompt: "",
  GlossaryExtractionSystemPrompt: "",
  GlossaryExtractionUserPrompt: ""
};

const promptTemplateFields: Array<{ key: keyof PromptTemplateConfig; label: string; placeholder: string }> = [
  { key: "SystemPrompt", label: "系统提示词模板", placeholder: "留空使用内置默认；可用 {TargetLanguage}、{StyleInstruction}、{GameContext}。" },
  { key: "GlossarySystemPolicy", label: "术语系统约束", placeholder: "留空使用内置默认，通常由 {GlossarySystemPolicy} 引入。" },
  { key: "BatchUserPrompt", label: "批量翻译用户提示词", placeholder: "必须保留 {InputJson}；留空使用内置默认。" },
  { key: "GlossaryTermsSection", label: "术语条目片段", placeholder: "必须保留 {GlossaryTermsJson}；留空使用内置默认。" },
  { key: "CurrentItemContextSection", label: "当前文本上下文片段", placeholder: "必须保留 {ItemContextsJson}；留空使用内置默认。" },
  { key: "ItemHintsSection", label: "短文本提示片段", placeholder: "必须保留 {ItemHintsJson}；留空使用内置默认。" },
  { key: "ContextExamplesSection", label: "历史上下文示例片段", placeholder: "必须保留 {ContextExamplesJson}；留空使用内置默认。" },
  { key: "GlossaryRepairPrompt", label: "术语修复提示词", placeholder: "必须保留 {SourceText}、{InvalidTranslation}、{FailureReason}。" },
  { key: "QualityRepairPrompt", label: "质量修复提示词", placeholder: "必须保留 {SourceText}、{InvalidTranslation}、{FailureReason}、{RepairContextJson}。" },
  { key: "GlossaryExtractionSystemPrompt", label: "术语抽取系统提示词", placeholder: "留空使用内置默认。" },
  { key: "GlossaryExtractionUserPrompt", label: "术语抽取用户提示词", placeholder: "必须保留 {RowsJson}；留空使用内置默认。" }
];

const translationQualityDefaults: TranslationQualityConfig = {
  Enabled: true,
  Mode: "balanced",
  AllowAlreadyTargetLanguageSource: true,
  EnableRepair: true,
  MaxRetryCount: 3,
  RejectGeneratedOuterSymbols: true,
  RejectUntranslatedLatinUiText: true,
  RejectShortSettingValue: true,
  RejectLiteralStateTranslation: true,
  RejectSameParentOptionCollision: true,
  ShortSettingValueMinSourceLength: 4,
  ShortSettingValueMaxTranslationTextElements: 1
};

const pages: Array<{ key: PageKey; label: string; icon: Component }> = [
  { key: "library", label: "游戏库", icon: Gamepad2 },
  { key: "install", label: "自动安装", icon: Download },
  { key: "config", label: "插件配置", icon: Settings },
  { key: "translations", label: "译文编辑", icon: Database },
  { key: "about", label: "关于工具箱", icon: Info }
];

const installModeOptions: Array<{ value: InstallMode; title: string; detail: string; badge?: string }> = [
  { value: "Full", title: "完整安装", detail: "安装 BepInEx、插件和必要依赖。", badge: "推荐" },
  { value: "PluginOnly", title: "只更新插件", detail: "保留现有 BepInEx，只替换插件文件。" },
  { value: "LlamaCppBackendOnly", title: "仅安装本地模型后端", detail: "只写入 llama.cpp CUDA 或 Vulkan 后端包。" }
];
const libraryAccentOptions: LibraryAccent[] = ["blue", "green", "amber", "rose"];

const activePage = ref<PageKey>("library");
const theme = ref<ThemeMode>(readStoredTheme());
const sidebarCollapsed = ref(readStoredBool(sidebarStorageKey, false));
const libraryLayout = ref<LibraryLayout>(readStoredOption(libraryLayoutStorageKey, ["grid", "list"], "grid"));
const libraryPosterSize = ref<LibraryPosterSize>(readStoredOption(libraryPosterSizeStorageKey, ["compact", "normal", "large"], "normal"));
const libraryAccent = ref<LibraryAccent>(readStoredOption(libraryAccentStorageKey, ["blue", "green", "amber", "rose"], "blue"));
const games = ref<GameLibraryEntry[]>(readStoredGames());
const selectedGameId = ref(readStoredString(selectedGameStorageKey));
const manualGameRoot = ref("");
const libraryMessage = ref("游戏库默认不选择任何目录，请添加并选择一个游戏。");
const libraryBusy = ref(false);

const packageVersion = ref("0.1.1");
const installMode = ref<InstallMode>("Full");
const includeLlamaCppBackend = ref(false);
const llamaCppBackend = ref("Cuda13");
const installPlan = ref<InstallPlan | null>(null);
const installBusy = ref(false);
const installMessage = ref("安装页只使用游戏库当前选中的游戏。");

const pluginConfigBase = ref<UpdateConfigRequest>({});
const pluginSettingsPath = ref("");
const pluginConfigLoading = ref(false);
const pluginConfigMessage = ref("选择游戏后读取插件配置。");
const pluginConfigDirty = ref(false);
const listeningHotkeyField = ref<HotkeyField | null>(null);
const isPickingFontFile = ref(false);

const pluginForm = reactive({
  TargetLanguage: "zh-Hans",
  GameTitle: "",
  Style: "Localized",
  ProviderKind: "OpenAI",
  Enabled: true,
  AutoOpenControlPanel: true,
  HttpPort: 48110,
  MaxConcurrentRequests: 4,
  RequestsPerMinute: 60,
  MaxBatchCharacters: 1800,
  RequestTimeoutSeconds: 30,
  OpenControlPanelHotkey: "Alt+H",
  ToggleTranslationHotkey: "Alt+F",
  ForceScanHotkey: "Alt+G",
  ToggleFontHotkey: "Alt+D",
  EnableUgui: true,
  EnableTmp: true,
  EnableImgui: true,
  ScanIntervalMilliseconds: 100,
  MaxScanTargetsPerTick: 256,
  MaxWritebacksPerFrame: 32,
  MaxSourceTextLength: 2000,
  IgnoreInvisibleText: true,
  SkipNumericSymbolText: true,
  EnableCacheLookup: true,
  EnableTranslationDebugLogs: false,
  EnableTranslationContext: true,
  TranslationContextMaxExamples: 4,
  TranslationContextMaxCharacters: 1200,
  EnableGlossary: true,
  EnableAutoTermExtraction: false,
  GlossaryMaxTerms: 16,
  GlossaryMaxCharacters: 1200,
  ManualEditsOverrideAi: true,
  ReapplyRememberedTranslations: true,
  EnableFontReplacement: true,
  ReplaceUguiFonts: true,
  ReplaceTmpFonts: true,
  ReplaceImguiFonts: true,
  AutoUseCjkFallbackFonts: true,
  ReplacementFontName: "",
  ReplacementFontFile: "",
  FontSamplingPointSize: 90,
  FontSizeAdjustmentMode: "Disabled",
  FontSizeAdjustmentValue: 0,
  EnableTmpNativeAutoSize: false,
  PromptTemplates: { ...promptTemplateDefaults },
  TranslationQuality: { ...translationQualityDefaults },
  LlamaCpp: {
    ModelPath: "",
    ContextSize: 4096,
    GpuLayers: 999,
    ParallelSlots: 1,
    BatchSize: 2048,
    UBatchSize: 512,
    FlashAttentionMode: "auto",
    AutoStartOnStartup: false
  },
  TextureImageTranslation: {
    Enabled: false,
    BaseUrl: "https://api.openai.com",
    EditEndpoint: "/v1/images/edits",
    VisionEndpoint: "/v1/responses",
    ImageModel: "gpt-image-2",
    VisionModel: "gpt-5.4-mini",
    Quality: "medium",
    TimeoutSeconds: 180,
    MaxConcurrentRequests: 1,
    EnableVisionConfirmation: true
  }
});

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

const selectedGame = computed(() => games.value.find((game) => game.Id === selectedGameId.value) ?? null);
const selectedInspection = computed(() => selectedGame.value?.Inspection ?? null);
const selectedGameRoot = computed(() => selectedGame.value?.Root ?? "");
const pageTitle = computed(() => pages.find((page) => page.key === activePage.value)?.label ?? "工具箱");
const themeText = computed(() => theme.value === "system" ? "跟随系统" : theme.value === "light" ? "浅色" : "深色");
const themeIcon = computed(() => theme.value === "system" ? MonitorCog : theme.value === "light" ? Sun : Moon);
const titlebarSubtitle = computed(() => selectedGame.value ? selectedGame.value.Name : "未选择游戏");
const runtimeSummary = computed(() => selectedInspection.value
  ? `${selectedInspection.value.Backend} ${selectedInspection.value.Architecture}`
  : "未检测");
const selectedGameIsReady = computed(() => Boolean(selectedInspection.value?.IsValidUnityGame));
const libraryClass = computed(() => `library-${libraryLayout.value} poster-${libraryPosterSize.value} accent-${libraryAccent.value}`);
const installSteps = computed(() => [
  {
    title: "游戏库目标",
    detail: selectedGame.value?.Root || "先在游戏库添加并选择游戏目录。",
    status: selectedGame.value ? "已选择" : "未选择"
  },
  {
    title: "运行环境",
    detail: selectedInspection.value
      ? `BepInEx：${selectedInspection.value.BepInExVersion ?? "未安装"}，插件：${selectedInspection.value.PluginInstalled ? "已存在" : "未安装"}`
      : "在游戏库检测目录后显示运行时信息。",
    status: selectedInspection.value ? runtimeSummary.value : "待检测"
  },
  {
    title: "准备安装包",
    detail: "根据游戏运行时选择 Mono、IL2CPP 或 BepInEx5 包。",
    status: installPlan.value?.PluginPackageName ?? "待准备"
  },
  {
    title: "预览文件变更",
    detail: "列出新增、覆盖、备份和受保护的本地数据。",
    status: installPlan.value ? "可查看" : "待生成"
  }
]);
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

function readStoredTheme(): ThemeMode {
  try {
    const saved = window.localStorage.getItem(themeStorageKey);
    return saved === "system" || saved === "light" || saved === "dark" ? saved : "system";
  } catch {
    return "system";
  }
}

function writeStoredTheme(value: ThemeMode): void {
  try {
    window.localStorage.setItem(themeStorageKey, value);
  } catch {
    // WebView2 NavigateToString can reject storage access before Vue mounts.
  }
}

function readStoredString(key: string): string {
  try {
    return window.localStorage.getItem(key) ?? "";
  } catch {
    return "";
  }
}

function writeStoredString(key: string, value: string): void {
  try {
    if (value) {
      window.localStorage.setItem(key, value);
    } else {
      window.localStorage.removeItem(key);
    }
  } catch {
    // WebView2 NavigateToString can reject storage access before Vue mounts.
  }
}

function readStoredBool(key: string, fallback: boolean): boolean {
  const saved = readStoredString(key);
  return saved === "true" ? true : saved === "false" ? false : fallback;
}

function readStoredOption<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  const saved = readStoredString(key);
  return allowed.includes(saved as T) ? saved as T : fallback;
}

function readStoredGames(): GameLibraryEntry[] {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(gameLibraryStorageKey) ?? "[]");
    if (!Array.isArray(parsed)) {
      return [];
    }

    return parsed
      .filter((item): item is GameLibraryEntry =>
        Boolean(item) &&
        typeof item.Id === "string" &&
        typeof item.Root === "string" &&
        typeof item.Name === "string")
      .map((item) => ({
        ...item,
        Inspection: item.Inspection ?? null
      }));
  } catch {
    window.localStorage.removeItem(gameLibraryStorageKey);
    return [];
  }
}

function persistGames(): void {
  writeStoredString(gameLibraryStorageKey, JSON.stringify(games.value));
}

function persistSelectedGame(): void {
  writeStoredString(selectedGameStorageKey, selectedGameId.value);
}

function effectiveTheme(value: ThemeMode): "light" | "dark" {
  if (value === "system") {
    return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  return value;
}

function applyTheme(): void {
  document.documentElement.dataset.theme = effectiveTheme(theme.value);
}

function cycleTheme(): void {
  const order: ThemeMode[] = ["system", "light", "dark"];
  theme.value = order[(order.indexOf(theme.value) + 1) % order.length];
}

function toggleSidebar(): void {
  sidebarCollapsed.value = !sidebarCollapsed.value;
}

function setPage(page: PageKey): void {
  activePage.value = page;
}

function dragWindow(event: MouseEvent): void {
  const target = event.target as HTMLElement | null;
  if (target?.closest("button,input,select,textarea,a")) {
    return;
  }

  void invokeToolbox("windowDrag");
}

function minimizeWindow(): void {
  void invokeToolbox("windowMinimize");
}

function toggleWindowMaximize(): void {
  void invokeToolbox("windowToggleMaximize");
}

function closeWindow(): void {
  void invokeToolbox("windowClose");
}

function rootDisplayName(root: string): string {
  return root.split(/[\\/]/).filter(Boolean).pop() || "Unity 游戏";
}

function createGameId(root: string): string {
  const normalized = root.trim().toLowerCase();
  let hash = 0;
  for (let index = 0; index < normalized.length; index += 1) {
    hash = ((hash << 5) - hash + normalized.charCodeAt(index)) | 0;
  }
  return `game-${Math.abs(hash)}`;
}

function upsertGame(root: string, inspection: GameInspection | null): GameLibraryEntry {
  const normalizedRoot = inspection?.GameRoot || root.trim();
  const id = createGameId(normalizedRoot);
  const now = new Date().toISOString();
  const existing = games.value.find((game) => game.Id === id);
  const entry: GameLibraryEntry = {
    Id: id,
    Root: normalizedRoot,
    Name: inspection?.GameName?.trim() || existing?.Name || rootDisplayName(normalizedRoot),
    AddedUtc: existing?.AddedUtc ?? now,
    UpdatedUtc: now,
    Inspection: inspection
  };
  games.value = existing
    ? games.value.map((game) => game.Id === id ? entry : game)
    : [entry, ...games.value];
  selectedGameId.value = id;
  persistGames();
  persistSelectedGame();
  return entry;
}

async function inspectRoot(root: string): Promise<GameInspection | null> {
  const trimmed = root.trim();
  if (!trimmed) {
    libraryMessage.value = "请先填写或选择游戏目录。";
    return null;
  }

  const inspection = await invokeToolbox<GameInspection>("inspectGame", { gameRoot: trimmed });
  return inspection;
}

async function addManualGame(): Promise<void> {
  if (libraryBusy.value) {
    return;
  }

  libraryBusy.value = true;
  try {
    const inspection = await inspectRoot(manualGameRoot.value);
    if (!inspection) {
      return;
    }

    const entry = upsertGame(manualGameRoot.value, inspection);
    manualGameRoot.value = "";
    libraryMessage.value = inspection.IsValidUnityGame
      ? `已添加并选择：${entry.Name}`
      : `目录已加入游戏库，但没有识别到有效 Unity 游戏：${entry.Root}`;
  } catch (error) {
    libraryMessage.value = error instanceof Error ? error.message : "检测游戏目录失败。";
  } finally {
    libraryBusy.value = false;
  }
}

async function pickAndAddGame(): Promise<void> {
  if (libraryBusy.value) {
    return;
  }

  try {
    const picked = await invokeToolbox<string>("pickGameDirectory", { initialDirectory: selectedGameRoot.value });
    if (!picked) {
      libraryMessage.value = "已取消选择目录。";
      return;
    }

    manualGameRoot.value = picked;
    await addManualGame();
  } catch (error) {
    libraryMessage.value = error instanceof Error ? error.message : "打开目录选择器失败。";
  }
}

function selectGame(id: string): void {
  selectedGameId.value = id;
  persistSelectedGame();
  installPlan.value = null;
  libraryMessage.value = `当前游戏：${selectedGame.value?.Name ?? "未选择"}`;
}

async function refreshSelectedGame(): Promise<void> {
  if (!selectedGame.value || libraryBusy.value) {
    libraryMessage.value = "请先在游戏库选择一个游戏。";
    return;
  }

  libraryBusy.value = true;
  try {
    const inspection = await inspectRoot(selectedGame.value.Root);
    if (!inspection) {
      return;
    }

    const entry = upsertGame(selectedGame.value.Root, inspection);
    libraryMessage.value = inspection.IsValidUnityGame
      ? `检测完成：${entry.Name}，${inspection.Backend} ${inspection.Architecture}`
      : "没有识别到有效 Unity 游戏目录。";
  } catch (error) {
    libraryMessage.value = error instanceof Error ? error.message : "检测目录失败。";
  } finally {
    libraryBusy.value = false;
  }
}

function removeGame(id: string): void {
  games.value = games.value.filter((game) => game.Id !== id);
  if (selectedGameId.value === id) {
    selectedGameId.value = "";
    installPlan.value = null;
  }
  persistGames();
  persistSelectedGame();
}

async function previewInstallPlan(): Promise<void> {
  if (!selectedGame.value) {
    installMessage.value = "请先到游戏库添加并选择游戏目录。";
    activePage.value = "library";
    return;
  }

  installBusy.value = true;
  try {
    installPlan.value = await invokeToolbox<InstallPlan>("createInstallPlan", {
      gameRoot: selectedGame.value.Root,
      packageVersion: packageVersion.value,
      mode: installMode.value,
      includeLlamaCppBackend: includeLlamaCppBackend.value,
      llamaCppBackend: llamaCppBackend.value
    });
    upsertGame(selectedGame.value.Root, installPlan.value.Inspection);
    installMessage.value = `安装计划已生成：${installPlan.value.PluginPackageName}`;
  } catch (error) {
    installMessage.value = error instanceof Error ? error.message : "生成安装计划失败。";
  } finally {
    installBusy.value = false;
  }
}

function numberValue(value: number | string | null | undefined, fallback = 0): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function emptyToNull(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length ? trimmed : null;
}

function enumName(value: unknown, fallback: string, names: string[]): string {
  if (typeof value === "string" && value.trim()) {
    return value;
  }
  if (typeof value === "number" && Number.isInteger(value) && names[value]) {
    return names[value];
  }
  return fallback;
}

function applyPluginConfig(config: UpdateConfigRequest | null | undefined): void {
  const styleNames = ["Faithful", "Natural", "Localized", "UiConcise"];
  const providerKindNames = ["OpenAI", "DeepSeek", "OpenAICompatible", "LlamaCpp"];
  const fontModeNames = ["Disabled", "Points", "Percent"];
  const llama = config?.LlamaCpp;
  const texture = config?.TextureImageTranslation;
  const prompts = (config?.PromptTemplates ?? {}) as Partial<Record<keyof PromptTemplateConfig, string | null>>;
  const quality = config?.TranslationQuality ?? translationQualityDefaults;

  pluginForm.TargetLanguage = config?.TargetLanguage ?? "zh-Hans";
  pluginForm.GameTitle = config?.GameTitle ?? "";
  pluginForm.Style = enumName(config?.Style, "Localized", styleNames);
  pluginForm.ProviderKind = enumName(config?.ProviderKind, "OpenAI", providerKindNames);
  pluginForm.Enabled = config?.Enabled ?? true;
  pluginForm.AutoOpenControlPanel = config?.AutoOpenControlPanel ?? true;
  pluginForm.HttpPort = config?.HttpPort ?? 48110;
  pluginForm.MaxConcurrentRequests = config?.MaxConcurrentRequests ?? 4;
  pluginForm.RequestsPerMinute = config?.RequestsPerMinute ?? 60;
  pluginForm.MaxBatchCharacters = config?.MaxBatchCharacters ?? 1800;
  pluginForm.RequestTimeoutSeconds = config?.RequestTimeoutSeconds ?? 30;
  pluginForm.OpenControlPanelHotkey = config?.OpenControlPanelHotkey ?? "Alt+H";
  pluginForm.ToggleTranslationHotkey = config?.ToggleTranslationHotkey ?? "Alt+F";
  pluginForm.ForceScanHotkey = config?.ForceScanHotkey ?? "Alt+G";
  pluginForm.ToggleFontHotkey = config?.ToggleFontHotkey ?? "Alt+D";
  pluginForm.EnableUgui = config?.EnableUgui ?? true;
  pluginForm.EnableTmp = config?.EnableTmp ?? true;
  pluginForm.EnableImgui = config?.EnableImgui ?? true;
  pluginForm.ScanIntervalMilliseconds = config?.ScanIntervalMilliseconds ?? 100;
  pluginForm.MaxScanTargetsPerTick = config?.MaxScanTargetsPerTick ?? 256;
  pluginForm.MaxWritebacksPerFrame = config?.MaxWritebacksPerFrame ?? 32;
  pluginForm.MaxSourceTextLength = config?.MaxSourceTextLength ?? 2000;
  pluginForm.IgnoreInvisibleText = config?.IgnoreInvisibleText ?? true;
  pluginForm.SkipNumericSymbolText = config?.SkipNumericSymbolText ?? true;
  pluginForm.EnableCacheLookup = config?.EnableCacheLookup ?? true;
  pluginForm.EnableTranslationDebugLogs = config?.EnableTranslationDebugLogs ?? false;
  pluginForm.EnableTranslationContext = config?.EnableTranslationContext ?? true;
  pluginForm.TranslationContextMaxExamples = config?.TranslationContextMaxExamples ?? 4;
  pluginForm.TranslationContextMaxCharacters = config?.TranslationContextMaxCharacters ?? 1200;
  pluginForm.EnableGlossary = config?.EnableGlossary ?? true;
  pluginForm.EnableAutoTermExtraction = config?.EnableAutoTermExtraction ?? false;
  pluginForm.GlossaryMaxTerms = config?.GlossaryMaxTerms ?? 16;
  pluginForm.GlossaryMaxCharacters = config?.GlossaryMaxCharacters ?? 1200;
  pluginForm.ManualEditsOverrideAi = config?.ManualEditsOverrideAi ?? true;
  pluginForm.ReapplyRememberedTranslations = config?.ReapplyRememberedTranslations ?? true;
  pluginForm.EnableFontReplacement = config?.EnableFontReplacement ?? true;
  pluginForm.ReplaceUguiFonts = config?.ReplaceUguiFonts ?? true;
  pluginForm.ReplaceTmpFonts = config?.ReplaceTmpFonts ?? true;
  pluginForm.ReplaceImguiFonts = config?.ReplaceImguiFonts ?? true;
  pluginForm.AutoUseCjkFallbackFonts = config?.AutoUseCjkFallbackFonts ?? true;
  pluginForm.ReplacementFontName = config?.ReplacementFontName ?? "";
  pluginForm.ReplacementFontFile = config?.ReplacementFontFile ?? "";
  pluginForm.FontSamplingPointSize = config?.FontSamplingPointSize ?? 90;
  pluginForm.FontSizeAdjustmentMode = enumName(config?.FontSizeAdjustmentMode, "Disabled", fontModeNames);
  pluginForm.FontSizeAdjustmentValue = config?.FontSizeAdjustmentValue ?? 0;
  pluginForm.EnableTmpNativeAutoSize = config?.EnableTmpNativeAutoSize ?? false;
  for (const field of promptTemplateFields) {
    pluginForm.PromptTemplates[field.key] = prompts[field.key] ?? "";
  }
  pluginForm.TranslationQuality.Enabled = quality.Enabled ?? translationQualityDefaults.Enabled;
  pluginForm.TranslationQuality.Mode = quality.Mode ?? translationQualityDefaults.Mode;
  pluginForm.TranslationQuality.AllowAlreadyTargetLanguageSource = quality.AllowAlreadyTargetLanguageSource ?? translationQualityDefaults.AllowAlreadyTargetLanguageSource;
  pluginForm.TranslationQuality.EnableRepair = quality.EnableRepair ?? translationQualityDefaults.EnableRepair;
  pluginForm.TranslationQuality.MaxRetryCount = quality.MaxRetryCount ?? translationQualityDefaults.MaxRetryCount;
  pluginForm.TranslationQuality.RejectGeneratedOuterSymbols = quality.RejectGeneratedOuterSymbols ?? translationQualityDefaults.RejectGeneratedOuterSymbols;
  pluginForm.TranslationQuality.RejectUntranslatedLatinUiText = quality.RejectUntranslatedLatinUiText ?? translationQualityDefaults.RejectUntranslatedLatinUiText;
  pluginForm.TranslationQuality.RejectShortSettingValue = quality.RejectShortSettingValue ?? translationQualityDefaults.RejectShortSettingValue;
  pluginForm.TranslationQuality.RejectLiteralStateTranslation = quality.RejectLiteralStateTranslation ?? translationQualityDefaults.RejectLiteralStateTranslation;
  pluginForm.TranslationQuality.RejectSameParentOptionCollision = quality.RejectSameParentOptionCollision ?? translationQualityDefaults.RejectSameParentOptionCollision;
  pluginForm.TranslationQuality.ShortSettingValueMinSourceLength = quality.ShortSettingValueMinSourceLength ?? translationQualityDefaults.ShortSettingValueMinSourceLength;
  pluginForm.TranslationQuality.ShortSettingValueMaxTranslationTextElements = quality.ShortSettingValueMaxTranslationTextElements ?? translationQualityDefaults.ShortSettingValueMaxTranslationTextElements;
  pluginForm.LlamaCpp.ModelPath = llama?.ModelPath ?? "";
  pluginForm.LlamaCpp.ContextSize = llama?.ContextSize ?? 4096;
  pluginForm.LlamaCpp.GpuLayers = llama?.GpuLayers ?? 999;
  pluginForm.LlamaCpp.ParallelSlots = llama?.ParallelSlots ?? 1;
  pluginForm.LlamaCpp.BatchSize = llama?.BatchSize ?? 2048;
  pluginForm.LlamaCpp.UBatchSize = llama?.UBatchSize ?? 512;
  pluginForm.LlamaCpp.FlashAttentionMode = llama?.FlashAttentionMode ?? "auto";
  pluginForm.LlamaCpp.AutoStartOnStartup = llama?.AutoStartOnStartup ?? false;
  pluginForm.TextureImageTranslation.Enabled = texture?.Enabled ?? false;
  pluginForm.TextureImageTranslation.BaseUrl = texture?.BaseUrl ?? "https://api.openai.com";
  pluginForm.TextureImageTranslation.EditEndpoint = texture?.EditEndpoint ?? "/v1/images/edits";
  pluginForm.TextureImageTranslation.VisionEndpoint = texture?.VisionEndpoint ?? "/v1/responses";
  pluginForm.TextureImageTranslation.ImageModel = texture?.ImageModel ?? "gpt-image-2";
  pluginForm.TextureImageTranslation.VisionModel = texture?.VisionModel ?? "gpt-5.4-mini";
  pluginForm.TextureImageTranslation.Quality = texture?.Quality ?? "medium";
  pluginForm.TextureImageTranslation.TimeoutSeconds = texture?.TimeoutSeconds ?? 180;
  pluginForm.TextureImageTranslation.MaxConcurrentRequests = texture?.MaxConcurrentRequests ?? 1;
  pluginForm.TextureImageTranslation.EnableVisionConfirmation = texture?.EnableVisionConfirmation ?? true;
  pluginConfigDirty.value = false;
}

function readPluginForm(): UpdateConfigRequest {
  const promptTemplates = Object.fromEntries(
    promptTemplateFields.map((field) => [field.key, emptyToNull(pluginForm.PromptTemplates[field.key])])
  ) as unknown as PromptTemplateConfig;
  const translationQuality: TranslationQualityConfig = {
    Enabled: pluginForm.TranslationQuality.Enabled,
    Mode: pluginForm.TranslationQuality.Mode,
    AllowAlreadyTargetLanguageSource: pluginForm.TranslationQuality.AllowAlreadyTargetLanguageSource,
    EnableRepair: pluginForm.TranslationQuality.EnableRepair,
    MaxRetryCount: numberValue(pluginForm.TranslationQuality.MaxRetryCount, 3),
    RejectGeneratedOuterSymbols: pluginForm.TranslationQuality.RejectGeneratedOuterSymbols,
    RejectUntranslatedLatinUiText: pluginForm.TranslationQuality.RejectUntranslatedLatinUiText,
    RejectShortSettingValue: pluginForm.TranslationQuality.RejectShortSettingValue,
    RejectLiteralStateTranslation: pluginForm.TranslationQuality.RejectLiteralStateTranslation,
    RejectSameParentOptionCollision: pluginForm.TranslationQuality.RejectSameParentOptionCollision,
    ShortSettingValueMinSourceLength: numberValue(pluginForm.TranslationQuality.ShortSettingValueMinSourceLength, 4),
    ShortSettingValueMaxTranslationTextElements: numberValue(pluginForm.TranslationQuality.ShortSettingValueMaxTranslationTextElements, 1)
  };
  const texture: TextureImageTranslationConfig = {
    Enabled: pluginForm.TextureImageTranslation.Enabled,
    BaseUrl: pluginForm.TextureImageTranslation.BaseUrl,
    EditEndpoint: pluginForm.TextureImageTranslation.EditEndpoint,
    VisionEndpoint: pluginForm.TextureImageTranslation.VisionEndpoint,
    ImageModel: pluginForm.TextureImageTranslation.ImageModel,
    VisionModel: pluginForm.TextureImageTranslation.VisionModel,
    Quality: pluginForm.TextureImageTranslation.Quality,
    TimeoutSeconds: numberValue(pluginForm.TextureImageTranslation.TimeoutSeconds, 180),
    MaxConcurrentRequests: numberValue(pluginForm.TextureImageTranslation.MaxConcurrentRequests, 1),
    EnableVisionConfirmation: pluginForm.TextureImageTranslation.EnableVisionConfirmation
  };

  return {
    TargetLanguage: pluginForm.TargetLanguage,
    GameTitle: emptyToNull(pluginForm.GameTitle),
    Style: pluginForm.Style,
    ProviderKind: pluginForm.ProviderKind,
    Enabled: pluginForm.Enabled,
    AutoOpenControlPanel: pluginForm.AutoOpenControlPanel,
    HttpPort: numberValue(pluginForm.HttpPort, 48110),
    MaxConcurrentRequests: numberValue(pluginForm.MaxConcurrentRequests, 4),
    RequestsPerMinute: numberValue(pluginForm.RequestsPerMinute, 60),
    MaxBatchCharacters: numberValue(pluginForm.MaxBatchCharacters, 1800),
    RequestTimeoutSeconds: numberValue(pluginForm.RequestTimeoutSeconds, 30),
    OpenControlPanelHotkey: pluginForm.OpenControlPanelHotkey,
    ToggleTranslationHotkey: pluginForm.ToggleTranslationHotkey,
    ForceScanHotkey: pluginForm.ForceScanHotkey,
    ToggleFontHotkey: pluginForm.ToggleFontHotkey,
    EnableUgui: pluginForm.EnableUgui,
    EnableTmp: pluginForm.EnableTmp,
    EnableImgui: pluginForm.EnableImgui,
    ScanIntervalMilliseconds: numberValue(pluginForm.ScanIntervalMilliseconds, 100),
    MaxScanTargetsPerTick: numberValue(pluginForm.MaxScanTargetsPerTick, 256),
    MaxWritebacksPerFrame: numberValue(pluginForm.MaxWritebacksPerFrame, 32),
    MaxSourceTextLength: numberValue(pluginForm.MaxSourceTextLength, 2000),
    IgnoreInvisibleText: pluginForm.IgnoreInvisibleText,
    SkipNumericSymbolText: pluginForm.SkipNumericSymbolText,
    EnableCacheLookup: pluginForm.EnableCacheLookup,
    EnableTranslationDebugLogs: pluginForm.EnableTranslationDebugLogs,
    EnableTranslationContext: pluginForm.EnableTranslationContext,
    TranslationContextMaxExamples: numberValue(pluginForm.TranslationContextMaxExamples, 4),
    TranslationContextMaxCharacters: numberValue(pluginForm.TranslationContextMaxCharacters, 1200),
    EnableGlossary: pluginForm.EnableGlossary,
    EnableAutoTermExtraction: pluginForm.EnableAutoTermExtraction,
    GlossaryMaxTerms: numberValue(pluginForm.GlossaryMaxTerms, 16),
    GlossaryMaxCharacters: numberValue(pluginForm.GlossaryMaxCharacters, 1200),
    ManualEditsOverrideAi: pluginForm.ManualEditsOverrideAi,
    ReapplyRememberedTranslations: pluginForm.ReapplyRememberedTranslations,
    EnableFontReplacement: pluginForm.EnableFontReplacement,
    ReplaceUguiFonts: pluginForm.ReplaceUguiFonts,
    ReplaceTmpFonts: pluginForm.ReplaceTmpFonts,
    ReplaceImguiFonts: pluginForm.ReplaceImguiFonts,
    AutoUseCjkFallbackFonts: pluginForm.AutoUseCjkFallbackFonts,
    ReplacementFontName: emptyToNull(pluginForm.ReplacementFontName),
    ReplacementFontFile: emptyToNull(pluginForm.ReplacementFontFile),
    FontSamplingPointSize: numberValue(pluginForm.FontSamplingPointSize, 90),
    FontSizeAdjustmentMode: pluginForm.FontSizeAdjustmentMode,
    FontSizeAdjustmentValue: numberValue(pluginForm.FontSizeAdjustmentValue, 0),
    EnableTmpNativeAutoSize: pluginForm.EnableTmpNativeAutoSize,
    PromptTemplates: promptTemplates,
    TranslationQuality: translationQuality,
    LlamaCpp: {
      ModelPath: emptyToNull(pluginForm.LlamaCpp.ModelPath),
      ContextSize: numberValue(pluginForm.LlamaCpp.ContextSize, 4096),
      GpuLayers: numberValue(pluginForm.LlamaCpp.GpuLayers, 999),
      ParallelSlots: numberValue(pluginForm.LlamaCpp.ParallelSlots, 1),
      BatchSize: numberValue(pluginForm.LlamaCpp.BatchSize, 2048),
      UBatchSize: numberValue(pluginForm.LlamaCpp.UBatchSize, 512),
      FlashAttentionMode: pluginForm.LlamaCpp.FlashAttentionMode,
      AutoStartOnStartup: pluginForm.LlamaCpp.AutoStartOnStartup
    },
    TextureImageTranslation: texture
  };
}

function markPluginConfigDirty(): void {
  pluginConfigDirty.value = true;
}

function beginHotkeyCapture(field: HotkeyField): void {
  listeningHotkeyField.value = field;
}

function cancelHotkeyCapture(field: HotkeyField): void {
  if (listeningHotkeyField.value === field) {
    listeningHotkeyField.value = null;
  }
}

function hotkeyValue(field: HotkeyField): string {
  return listeningHotkeyField.value === field ? hotkeyListeningText : pluginForm[field];
}

function isModifierKey(key: string): boolean {
  return key === "Control" || key === "Shift" || key === "Alt" || key === "Meta";
}

function normalizeCapturedKey(key: string): string | null {
  if (/^[a-z]$/i.test(key) || /^[0-9]$/.test(key)) {
    return key.toUpperCase();
  }
  if (/^F([1-9]|1[0-2])$/i.test(key)) {
    return key.toUpperCase();
  }

  const knownKeys: Record<string, string> = {
    " ": "Space",
    Spacebar: "Space",
    Space: "Space",
    Enter: "Enter",
    Tab: "Tab",
    Backspace: "Backspace",
    Escape: "Escape",
    Esc: "Escape",
    Insert: "Insert",
    Delete: "Delete",
    Home: "Home",
    End: "End",
    PageUp: "PageUp",
    PageDown: "PageDown",
    ArrowUp: "UpArrow",
    ArrowDown: "DownArrow",
    ArrowLeft: "LeftArrow",
    ArrowRight: "RightArrow"
  };

  return knownKeys[key] ?? null;
}

function normalizeCapturedHotkey(event: KeyboardEvent): string | null {
  const key = normalizeCapturedKey(event.key);
  if (!key) {
    return null;
  }

  const parts: string[] = [];
  if (event.ctrlKey) {
    parts.push("Ctrl");
  }
  if (event.shiftKey) {
    parts.push("Shift");
  }
  if (event.altKey) {
    parts.push("Alt");
  }
  if (!parts.length) {
    return null;
  }

  parts.push(key);
  return parts.join("+");
}

function handleHotkeyKeydown(event: KeyboardEvent, field: HotkeyField): void {
  if (listeningHotkeyField.value !== field) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();

  if (event.key === "Escape") {
    cancelHotkeyCapture(field);
    return;
  }

  if (isModifierKey(event.key)) {
    return;
  }

  if ((event.key === "Backspace" || event.key === "Delete") && !event.ctrlKey && !event.shiftKey && !event.altKey) {
    pluginForm[field] = "None";
    listeningHotkeyField.value = null;
    markPluginConfigDirty();
    return;
  }

  if (!normalizeCapturedKey(event.key)) {
    pluginConfigMessage.value = "不支持这个按键，请换一个主键。";
    return;
  }

  const hotkey = normalizeCapturedHotkey(event);
  if (!hotkey) {
    pluginConfigMessage.value = "需要使用 Ctrl、Shift 或 Alt 组合键。";
    return;
  }

  pluginForm[field] = hotkey;
  listeningHotkeyField.value = null;
  markPluginConfigDirty();
}

async function pickReplacementFontFile(): Promise<void> {
  if (isPickingFontFile.value) {
    return;
  }

  isPickingFontFile.value = true;
  try {
    const result = await invokeToolbox<FontPickResult>("pickFontFile");
    if (result.Status === "selected" && result.FilePath) {
      pluginForm.ReplacementFontFile = result.FilePath;
      pluginForm.ReplacementFontName = result.FontName ?? "";
      markPluginConfigDirty();
      pluginConfigMessage.value = result.FontName ? `已选择字体：${result.FontName}` : "已选择字体文件。";
      return;
    }

    if (result.Status !== "cancelled") {
      pluginConfigMessage.value = result.Message || "选择字体文件失败。";
    }
  } catch (error) {
    pluginConfigMessage.value = error instanceof Error ? error.message : "选择字体文件失败。";
  } finally {
    isPickingFontFile.value = false;
  }
}

async function loadPluginConfig(): Promise<void> {
  if (!selectedGame.value) {
    pluginConfigMessage.value = "请先在游戏库选择游戏。";
    return;
  }

  pluginConfigLoading.value = true;
  try {
    const payload = await invokeToolbox<PluginConfigPayload>("loadPluginConfig", { gameRoot: selectedGame.value.Root });
    pluginConfigBase.value = payload.Config ?? {};
    pluginSettingsPath.value = payload.SettingsPath;
    applyPluginConfig(payload.Config);
    pluginConfigMessage.value = `已读取：${payload.SettingsPath}`;
  } catch (error) {
    pluginConfigMessage.value = error instanceof Error ? error.message : "读取插件配置失败。";
  } finally {
    pluginConfigLoading.value = false;
  }
}

async function savePluginConfig(): Promise<void> {
  if (!selectedGame.value) {
    pluginConfigMessage.value = "请先在游戏库选择游戏。";
    return;
  }

  pluginConfigLoading.value = true;
  try {
    const config = { ...pluginConfigBase.value, ...readPluginForm() };
    const payload = await invokeToolbox<PluginConfigPayload>("savePluginConfig", {
      gameRoot: selectedGame.value.Root,
      config
    });
    pluginConfigBase.value = payload.Config ?? config;
    pluginSettingsPath.value = payload.SettingsPath;
    applyPluginConfig(pluginConfigBase.value);
    pluginConfigMessage.value = `已直接覆盖保存：${payload.SettingsPath}`;
  } catch (error) {
    pluginConfigMessage.value = error instanceof Error ? error.message : "保存插件配置失败。";
  } finally {
    pluginConfigLoading.value = false;
  }
}

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
    const page = await invokeToolbox<TranslationCachePage>("queryTranslations", {
      gameRoot: selectedGame.value.Root,
      search: tableSearch.value,
      sort: sortColumn.value,
      direction: sortDirection.value,
      offset: 0,
      limit: 100,
      columnFilters: buildColumnFilterPayload()
    });
    rows.value = page.Items;
    totalCount.value = page.TotalCount;
    clearSelection();
    tableMessage.value = `共 ${page.TotalCount} 行，当前显示 ${page.Items.length} 行。`;
  } catch (error) {
    tableMessage.value = error instanceof Error ? error.message : "读取翻译缓存失败。";
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
    await invokeToolbox<TranslationCacheEntry>("updateTranslation", {
      gameRoot: selectedGame.value.Root,
      entry: row
    });
  }
  const savedCount = dirtyRows.size;
  dirtyRows.clear();
  await loadTranslations();
  tableMessage.value = `已保存 ${savedCount} 行修改。`;
}

async function deleteSelectedRows(): Promise<void> {
  if (!selectedGame.value || !selectedRows.value.length) {
    tableMessage.value = "请先选择要删除的行。";
    return;
  }

  if (!confirm(`删除选中的 ${selectedRows.value.length} 行已翻译文本？`)) {
    return;
  }

  const result = await invokeToolbox<DeleteResult>("deleteTranslations", {
    gameRoot: selectedGame.value.Root,
    entries: selectedRows.value
  });
  dirtyRows.clear();
  clearSelection();
  await loadTranslations();
  tableMessage.value = `已删除 ${result.DeletedCount} 行。`;
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
  const result = await invokeToolbox<TranslationCacheImportResult>("importTranslations", {
    gameRoot: selectedGame.value.Root,
    format,
    content: text
  });
  await loadTranslations();
  tableMessage.value = `已导入 ${result.ImportedCount} 行。${result.Errors.length ? ` ${result.Errors.length} 个错误。` : ""}`;
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

  const text = await invokeToolbox<string>("exportTranslations", {
    gameRoot: selectedGame.value.Root,
    format
  });
  const blob = new Blob([text], { type: format === "csv" ? "text/csv;charset=utf-8" : "application/json;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = `hunity-translations-${sanitizeFileNamePart(selectedGame.value.Name)}-${formatExportTimestamp()}.${format}`;
  anchor.click();
  URL.revokeObjectURL(url);
  tableMessage.value = "导出已开始。";
}

async function runMaintenance(): Promise<void> {
  if (!selectedGame.value) {
    tableMessage.value = "请先选择游戏。";
    return;
  }

  maintenanceResult.value = await invokeToolbox<DatabaseMaintenanceResult>("runDatabaseMaintenance", {
    gameRoot: selectedGame.value.Root,
    createBackup: true,
    runIntegrityCheck: true,
    reindex: true,
    vacuum: false
  });
  tableMessage.value = `维护完成：${maintenanceResult.value.Actions.join("、")}`;
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

  const page = await invokeToolbox<TranslationCacheFilterOptionPage>("getTranslationFilterOptions", {
    gameRoot: selectedGame.value.Root,
    column,
    search: tableSearch.value,
    optionSearch,
    limit: 80,
    columnFilters: buildColumnFilterPayload()
  });
  filterMenu.options = page.Items;
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

watch(theme, (value) => {
  writeStoredTheme(value);
  applyTheme();
});

watch(sidebarCollapsed, (value) => writeStoredString(sidebarStorageKey, String(value)));
watch(libraryLayout, (value) => writeStoredString(libraryLayoutStorageKey, value));
watch(libraryPosterSize, (value) => writeStoredString(libraryPosterSizeStorageKey, value));
watch(libraryAccent, (value) => writeStoredString(libraryAccentStorageKey, value));
watch(selectedGameId, persistSelectedGame);

watch([activePage, selectedGameRoot], ([page, root]) => {
  installPlan.value = null;
  if (page === "config" && root) {
    void loadPluginConfig();
  }
  if (page === "translations" && root) {
    void loadTranslations();
  }
});

onMounted(() => {
  applyTheme();
  document.addEventListener("click", handleDocumentClick);
});
</script>

<template>
  <div class="shell" :class="[{ 'sidebar-collapsed': sidebarCollapsed }, libraryClass]">
    <header class="window-titlebar" @mousedown.left="dragWindow" @dblclick="toggleWindowMaximize">
      <div class="window-brand">
        <img :src="brandIcon" alt="HUnityAutoTranslator" width="24" height="24">
        <strong>HUnityAutoTranslator 工具箱</strong>
        <span>{{ titlebarSubtitle }}</span>
      </div>
      <div class="window-context">
        <span>{{ pageTitle }}</span>
        <strong>{{ runtimeSummary }}</strong>
      </div>
      <div class="window-controls" @mousedown.stop @dblclick.stop>
        <button type="button" title="最小化" aria-label="最小化" @click="minimizeWindow"><Minus class="icon" /></button>
        <button type="button" title="最大化/还原" aria-label="最大化/还原" @click="toggleWindowMaximize"><Square class="icon" /></button>
        <button class="close-button" type="button" title="关闭" aria-label="关闭" @click="closeWindow"><X class="icon" /></button>
      </div>
    </header>

    <aside class="sidebar">
      <div class="brand">
        <img class="brand-logo" :src="brandIcon" alt="HUnity" width="36" height="36">
        <div class="brand-copy">
          <strong>HUnity</strong>
          <span>外部工具箱</span>
        </div>
      </div>

      <nav class="nav-list" aria-label="工具箱导航">
        <button
          v-for="page in pages"
          :key="page.key"
          class="nav-item"
          :class="{ active: activePage === page.key }"
          type="button"
          :title="page.label"
          @click="setPage(page.key)"
        >
          <component :is="page.icon" class="icon" aria-hidden="true" />
          <span>{{ page.label }}</span>
        </button>
      </nav>

      <div class="sidebar-footer">
        <button class="theme-cycle" type="button" :title="`主题：${themeText}`" @click="cycleTheme">
          <Palette class="icon" aria-hidden="true" />
          <span>主题</span>
          <strong>{{ themeText }}</strong>
          <component :is="themeIcon" class="icon" aria-hidden="true" />
        </button>
        <button class="collapse-button" type="button" :title="sidebarCollapsed ? '展开侧边栏' : '收起侧边栏'" @click="toggleSidebar">
          <PanelLeftOpen v-if="sidebarCollapsed" class="icon" aria-hidden="true" />
          <PanelLeftClose v-else class="icon" aria-hidden="true" />
          <span>{{ sidebarCollapsed ? "展开" : "收起" }}</span>
        </button>
      </div>
    </aside>

    <section class="workspace">
      <header class="page-hero">
        <div>
          <span class="eyebrow">{{ selectedGame ? selectedGame.Name : "未选择游戏" }}</span>
          <h1>{{ pageTitle }}</h1>
          <p>{{ selectedGame ? selectedGame.Root : "游戏库不会默认选择你电脑里的任何目录。" }}</p>
        </div>
        <div class="status-strip">
          <span class="pill">{{ runtimeSummary }}</span>
          <span class="pill">{{ selectedInspection?.BepInExVersion ?? "BepInEx 未检测" }}</span>
          <span class="pill">{{ selectedInspection?.PluginInstalled ? "插件已存在" : "插件未安装" }}</span>
        </div>
      </header>

      <main class="main">
        <section v-if="activePage === 'library'" class="page library-page">
          <div class="library-stage">
            <div class="library-hero">
              <div class="library-cover">
                <img :src="brandIcon" alt="" aria-hidden="true">
              </div>
              <div class="library-hero-copy">
                <span class="eyebrow">当前游戏</span>
                <h2>{{ selectedGame?.Name ?? "尚未选择" }}</h2>
                <p>{{ selectedGame?.Root ?? "添加目录后，安装、配置和译文编辑都会使用这里选中的游戏。" }}</p>
                <div class="hero-badges">
                  <span class="badge">{{ selectedGameIsReady ? "Unity 游戏" : "未检测" }}</span>
                  <span class="badge">{{ runtimeSummary }}</span>
                  <span class="badge">{{ games.length }} 个目录</span>
                </div>
              </div>
              <div class="library-hero-actions">
                <button class="button-primary" type="button" :disabled="libraryBusy" @click="pickAndAddGame">
                  <FolderPlus class="icon" />添加游戏目录
                </button>
                <button type="button" :disabled="!selectedGame || libraryBusy" @click="refreshSelectedGame">
                  <RefreshCw class="icon" />检测当前
                </button>
              </div>
            </div>

            <div class="library-toolbar">
              <label class="field manual-path">
                <span>手动添加目录</span>
                <input v-model="manualGameRoot" spellcheck="false" placeholder="例如 D:\Game\YourGame">
              </label>
              <button type="button" :disabled="libraryBusy || !manualGameRoot.trim()" @click="addManualGame">
                <CheckCircle2 class="icon" />添加并检测
              </button>
              <div class="segmented">
                <button type="button" :class="{ active: libraryLayout === 'grid' }" title="封面网格" @click="libraryLayout = 'grid'">
                  <Grid2X2 class="icon" />
                </button>
                <button type="button" :class="{ active: libraryLayout === 'list' }" title="列表" @click="libraryLayout = 'list'">
                  <List class="icon" />
                </button>
              </div>
              <select v-model="libraryPosterSize" title="库项目尺寸">
                <option value="compact">紧凑</option>
                <option value="normal">标准</option>
                <option value="large">大封面</option>
              </select>
              <div class="swatches" aria-label="游戏库强调色">
                <button v-for="accent in libraryAccentOptions" :key="accent" type="button" :class="['swatch', accent, { active: libraryAccent === accent }]" @click="libraryAccent = accent"></button>
              </div>
            </div>
            <p class="message">{{ libraryMessage }}</p>

            <div v-if="games.length" class="game-library">
              <article
                v-for="game in games"
                :key="game.Id"
                class="game-tile"
                :class="{ selected: selectedGameId === game.Id, invalid: game.Inspection && !game.Inspection.IsValidUnityGame }"
                @click="selectGame(game.Id)"
              >
                <div class="game-poster">
                  <img :src="brandIcon" alt="" aria-hidden="true">
                </div>
                <div class="game-info">
                  <strong>{{ game.Name }}</strong>
                  <span>{{ game.Root }}</span>
                  <div class="tile-badges">
                    <span>{{ game.Inspection?.Backend ?? "未检测" }}</span>
                    <span>{{ game.Inspection?.BepInExVersion ?? "BepInEx 未检测" }}</span>
                    <span>{{ game.Inspection?.PluginInstalled ? "插件已安装" : "插件未安装" }}</span>
                  </div>
                </div>
                <div class="game-actions" @click.stop>
                  <button type="button" title="选择" @click="selectGame(game.Id)"><CheckCircle2 class="icon" /></button>
                  <button class="button-danger" type="button" title="从游戏库移除" @click="removeGame(game.Id)"><Trash2 class="icon" /></button>
                </div>
              </article>
            </div>
            <div v-else class="empty-state">
              <Gamepad2 class="empty-icon" />
              <strong>游戏库为空</strong>
              <span>不会默认使用任何你电脑里的游戏目录。</span>
            </div>
          </div>
        </section>

        <section v-else-if="activePage === 'install'" class="page">
          <div v-if="!selectedGame" class="empty-state">
            <AlertTriangle class="empty-icon" />
            <strong>没有安装目标</strong>
            <span>请先在游戏库添加并选择游戏目录。</span>
            <button class="button-primary" type="button" @click="setPage('library')"><Gamepad2 class="icon" />打开游戏库</button>
          </div>

          <template v-else>
            <div class="panel install-target">
              <div>
                <span class="eyebrow">安装目标</span>
                <h2>{{ selectedGame.Name }}</h2>
                <p>{{ selectedGame.Root }}</p>
              </div>
              <div class="field compact-field">
                <span>插件版本</span>
                <input v-model="packageVersion" spellcheck="false">
              </div>
            </div>

            <div class="choice-grid">
              <button
                v-for="option in installModeOptions"
                :key="option.value"
                class="choice"
                :class="{ active: installMode === option.value }"
                type="button"
                @click="installMode = option.value"
              >
                <span v-if="option.badge" class="badge">{{ option.badge }}</span>
                <strong>{{ option.title }}</strong>
                <span>{{ option.detail }}</span>
              </button>
            </div>

            <div class="panel inline-settings">
              <label class="check"><input v-model="includeLlamaCppBackend" type="checkbox">同时准备 llama.cpp 后端包</label>
              <label class="field compact-field">
                <span>后端类型</span>
                <select v-model="llamaCppBackend" :disabled="!includeLlamaCppBackend">
                  <option value="Cuda13">CUDA 13</option>
                  <option value="Vulkan">Vulkan</option>
                </select>
              </label>
            </div>

            <div class="steps">
              <div v-for="(step, index) in installSteps" :key="step.title" class="step">
                <div class="step-index">{{ index + 1 }}</div>
                <div>
                  <strong>{{ step.title }}</strong>
                  <span>{{ step.detail }}</span>
                </div>
                <span class="badge">{{ step.status }}</span>
              </div>
            </div>

            <div class="actions">
              <button class="button-primary" type="button" :disabled="installBusy" @click="previewInstallPlan">
                <PackageCheck class="icon" />{{ installBusy ? "生成中" : "生成安装预览" }}
              </button>
              <button type="button" @click="setPage('library')"><Gamepad2 class="icon" />切换游戏</button>
              <button type="button"><ArchiveRestore class="icon" />回滚上次安装</button>
            </div>
            <p class="message">{{ installMessage }}</p>

            <div v-if="installPlan" class="panel">
              <h2>文件变更预览</h2>
              <div class="table-wrap">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>类型</th>
                      <th>来源</th>
                      <th>目标</th>
                      <th>说明</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr v-for="operation in installPlan.Operations" :key="`${operation.Kind}-${operation.DestinationPath}`">
                      <td>{{ operation.Kind }}</td>
                      <td>{{ operation.SourcePath || "-" }}</td>
                      <td>{{ operation.DestinationPath }}</td>
                      <td>{{ operation.Description }}</td>
                    </tr>
                  </tbody>
                </table>
              </div>
              <div class="actions">
                <button class="button-primary" type="button"><Download class="icon" />执行安装</button>
                <button type="button"><ShieldCheck class="icon" />查看受保护数据</button>
              </div>
            </div>
          </template>
        </section>

        <section v-else-if="activePage === 'config'" class="page">
          <div v-if="!selectedGame" class="empty-state">
            <Settings class="empty-icon" />
            <strong>没有配置目标</strong>
            <span>插件配置会直接覆盖所选游戏目录下的 cfg 文件。</span>
            <button class="button-primary" type="button" @click="setPage('library')"><Gamepad2 class="icon" />打开游戏库</button>
          </div>

          <template v-else>
            <div class="page-actions">
              <button type="button" :disabled="pluginConfigLoading" @click="loadPluginConfig"><RotateCcw class="icon" />重新读取</button>
              <button class="button-primary" type="button" :disabled="pluginConfigLoading" @click="savePluginConfig"><Save class="icon" />直接覆盖保存</button>
            </div>
            <p class="message">{{ pluginConfigMessage }}</p>

            <div class="form-stack" @input="markPluginConfigDirty" @change="markPluginConfigDirty">
              <section class="panel">
                <h2><SlidersHorizontal class="section-icon" />基础与性能</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.Enabled" type="checkbox">启用翻译</label>
                  <label class="check"><input v-model="pluginForm.AutoOpenControlPanel" type="checkbox">启动后自动打开面板</label>
                </div>
                <div class="field-grid four">
                  <label class="field"><span>目标语言</span><input v-model="pluginForm.TargetLanguage"></label>
                  <label class="field"><span>游戏标题</span><input v-model="pluginForm.GameTitle" placeholder="留空自动检测"></label>
                  <label class="field"><span>翻译风格</span><select v-model="pluginForm.Style"><option value="Faithful">忠实</option><option value="Natural">自然</option><option value="Localized">本地化</option><option value="UiConcise">UI 简短</option></select></label>
                  <label class="field"><span>翻译后端</span><select v-model="pluginForm.ProviderKind"><option value="OpenAI">OpenAI / 在线配置</option><option value="DeepSeek">DeepSeek</option><option value="OpenAICompatible">OpenAI 兼容</option><option value="LlamaCpp">llama.cpp</option></select></label>
                  <label class="field"><span>控制面板端口</span><input v-model.number="pluginForm.HttpPort" type="number" min="1" max="65535"></label>
                  <label class="field"><span>并发请求数</span><input v-model.number="pluginForm.MaxConcurrentRequests" type="number" min="1" max="100"></label>
                  <label class="field"><span>每分钟请求数</span><input v-model.number="pluginForm.RequestsPerMinute" type="number" min="1" max="600"></label>
                  <label class="field"><span>批量字符上限</span><input v-model.number="pluginForm.MaxBatchCharacters" type="number" min="1"></label>
                  <label class="field"><span>请求超时秒数</span><input v-model.number="pluginForm.RequestTimeoutSeconds" type="number" min="1"></label>
                </div>
              </section>

              <section class="panel">
                <h2><ScanLine class="section-icon" />运行与采集</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.EnableUgui" type="checkbox">采集 UGUI</label>
                  <label class="check"><input v-model="pluginForm.EnableTmp" type="checkbox">采集 TextMeshPro</label>
                  <label class="check"><input v-model="pluginForm.EnableImgui" type="checkbox">采集 IMGUI</label>
                </div>
                <div class="field-grid four">
                  <label class="field"><span><Timer class="field-icon" />扫描间隔 (毫秒)</span><input v-model.number="pluginForm.ScanIntervalMilliseconds" type="number" min="100"></label>
                  <label class="field"><span>每次扫描上限</span><input v-model.number="pluginForm.MaxScanTargetsPerTick" type="number" min="1"></label>
                  <label class="field"><span>每帧写回上限</span><input v-model.number="pluginForm.MaxWritebacksPerFrame" type="number" min="1"></label>
                  <label class="field"><span>原文长度上限</span><input v-model.number="pluginForm.MaxSourceTextLength" type="number" min="1"></label>
                </div>
              </section>

              <section class="panel">
                <h2><FileText class="section-icon" />文本策略与上下文</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.IgnoreInvisibleText" type="checkbox">忽略不可见文本</label>
                  <label class="check"><input v-model="pluginForm.SkipNumericSymbolText" type="checkbox">跳过数字/符号文本</label>
                  <label class="check"><input v-model="pluginForm.EnableCacheLookup" type="checkbox">启用缓存查找</label>
                  <label class="check"><input v-model="pluginForm.EnableTranslationDebugLogs" type="checkbox">输出翻译调试日志</label>
                  <label class="check"><input v-model="pluginForm.EnableTranslationContext" type="checkbox">启用上下文示例</label>
                  <label class="check"><input v-model="pluginForm.ManualEditsOverrideAi" type="checkbox">手动编辑优先</label>
                  <label class="check"><input v-model="pluginForm.ReapplyRememberedTranslations" type="checkbox">重新应用已记住译文</label>
                </div>
                <div class="field-grid four">
                  <label class="field"><span>上下文示例数</span><input v-model.number="pluginForm.TranslationContextMaxExamples" type="number" min="0"></label>
                  <label class="field"><span>上下文字符数</span><input v-model.number="pluginForm.TranslationContextMaxCharacters" type="number" min="0"></label>
                  <label class="field"><span>术语数量上限</span><input v-model.number="pluginForm.GlossaryMaxTerms" type="number" min="0"></label>
                  <label class="field"><span>术语字符上限</span><input v-model.number="pluginForm.GlossaryMaxCharacters" type="number" min="0"></label>
                </div>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.EnableGlossary" type="checkbox">启用术语库</label>
                  <label class="check"><input v-model="pluginForm.EnableAutoTermExtraction" type="checkbox">允许自动抽取术语</label>
                </div>
              </section>

              <section class="panel">
                <h2><Keyboard class="section-icon" />快捷键</h2>
                <div class="field-grid four">
                  <label class="field"><span>打开控制面板</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'OpenControlPanelHotkey' }" :value="hotkeyValue('OpenControlPanelHotkey')" readonly autocomplete="off" placeholder="Alt+H" @focus="beginHotkeyCapture('OpenControlPanelHotkey')" @click="beginHotkeyCapture('OpenControlPanelHotkey')" @blur="cancelHotkeyCapture('OpenControlPanelHotkey')" @keydown="handleHotkeyKeydown($event, 'OpenControlPanelHotkey')"></label>
                  <label class="field"><span>原文/译文切换</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleTranslationHotkey' }" :value="hotkeyValue('ToggleTranslationHotkey')" readonly autocomplete="off" placeholder="Alt+F" @focus="beginHotkeyCapture('ToggleTranslationHotkey')" @click="beginHotkeyCapture('ToggleTranslationHotkey')" @blur="cancelHotkeyCapture('ToggleTranslationHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleTranslationHotkey')"></label>
                  <label class="field"><span>全局扫描更新</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ForceScanHotkey' }" :value="hotkeyValue('ForceScanHotkey')" readonly autocomplete="off" placeholder="Alt+G" @focus="beginHotkeyCapture('ForceScanHotkey')" @click="beginHotkeyCapture('ForceScanHotkey')" @blur="cancelHotkeyCapture('ForceScanHotkey')" @keydown="handleHotkeyKeydown($event, 'ForceScanHotkey')"></label>
                  <label class="field"><span>字体状态切换</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleFontHotkey' }" :value="hotkeyValue('ToggleFontHotkey')" readonly autocomplete="off" placeholder="Alt+D" @focus="beginHotkeyCapture('ToggleFontHotkey')" @click="beginHotkeyCapture('ToggleFontHotkey')" @blur="cancelHotkeyCapture('ToggleFontHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleFontHotkey')"></label>
                </div>
              </section>

              <section class="panel">
                <h2><FileText class="section-icon" />提示词模板覆盖</h2>
                <div class="field-grid two">
                  <label v-for="field in promptTemplateFields" :key="field.key" class="field">
                    <span>{{ field.label }}</span>
                    <textarea v-model="pluginForm.PromptTemplates[field.key]" :placeholder="field.placeholder" spellcheck="false"></textarea>
                  </label>
                </div>
              </section>

              <section class="panel">
                <h2><CheckCircle2 class="section-icon" />译文质量检查</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.TranslationQuality.Enabled" type="checkbox">启用质量检查</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.AllowAlreadyTargetLanguageSource" type="checkbox">允许已是目标语言的短技术文本</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.EnableRepair" type="checkbox">质量失败后请求修复</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.RejectGeneratedOuterSymbols" type="checkbox">拒绝改变外层符号</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.RejectUntranslatedLatinUiText" type="checkbox">拒绝普通英文 UI 原样保留</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.RejectShortSettingValue" type="checkbox">拒绝设置值过短</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.RejectLiteralStateTranslation" type="checkbox">拒绝状态文本直译</label>
                  <label class="check"><input v-model="pluginForm.TranslationQuality.RejectSameParentOptionCollision" type="checkbox">拒绝同父级选项撞译</label>
                </div>
                <div class="field-grid four">
                  <label class="field"><span>质量预设</span><select v-model="pluginForm.TranslationQuality.Mode"><option value="balanced">balanced</option><option value="relaxed">relaxed</option><option value="strict">strict</option><option value="custom">custom</option></select></label>
                  <label class="field"><span>最大重试次数</span><input v-model.number="pluginForm.TranslationQuality.MaxRetryCount" type="number" min="0" max="10"></label>
                  <label class="field"><span>短译检查原文最小长度</span><input v-model.number="pluginForm.TranslationQuality.ShortSettingValueMinSourceLength" type="number" min="1" max="32"></label>
                  <label class="field"><span>短译允许最大字符数</span><input v-model.number="pluginForm.TranslationQuality.ShortSettingValueMaxTranslationTextElements" type="number" min="1" max="8"></label>
                </div>
              </section>

              <section class="panel">
                <h2><Type class="section-icon" />字体补字 fallback</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.EnableFontReplacement" type="checkbox">启用字体补字 fallback</label>
                  <label class="check"><input v-model="pluginForm.ReplaceUguiFonts" type="checkbox">UGUI 缺字补字</label>
                  <label class="check"><input v-model="pluginForm.ReplaceTmpFonts" type="checkbox">TextMeshPro fallback</label>
                  <label class="check"><input v-model="pluginForm.ReplaceImguiFonts" type="checkbox">IMGUI 临时补字</label>
                  <label class="check"><input v-model="pluginForm.AutoUseCjkFallbackFonts" type="checkbox">自动使用 CJK 字体</label>
                  <label class="check"><input v-model="pluginForm.EnableTmpNativeAutoSize" type="checkbox">TMP 原生字号适配</label>
                </div>
                <div class="field-grid four">
                  <label class="field"><span>手动字体名</span><input v-model="pluginForm.ReplacementFontName" placeholder="留空自动选择"></label>
                  <div class="field wide"><span>手动字体文件</span><div class="input-action-row"><input v-model="pluginForm.ReplacementFontFile" placeholder="TTF/OTF 路径"><button type="button" :disabled="isPickingFontFile" @click="pickReplacementFontFile"><FileText class="icon" />{{ isPickingFontFile ? "选择中" : "选择字体" }}</button></div></div>
                  <label class="field"><span>字体采样字号</span><input v-model.number="pluginForm.FontSamplingPointSize" type="number" min="16"></label>
                  <label class="field"><span>字号调整方式</span><select v-model="pluginForm.FontSizeAdjustmentMode"><option value="Disabled">关闭</option><option value="Percent">百分比例</option><option value="Points">固定增减</option></select></label>
                  <label class="field"><span>字号调整值</span><input v-model.number="pluginForm.FontSizeAdjustmentValue" type="number" step="0.1"></label>
                </div>
              </section>

              <section class="panel">
                <h2><HardDrive class="section-icon" />llama.cpp</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.LlamaCpp.AutoStartOnStartup" type="checkbox">下次启动游戏自动启动</label>
                </div>
                <div class="field-grid four">
                  <label class="field wide"><span>模型路径</span><input v-model="pluginForm.LlamaCpp.ModelPath" placeholder="D:\Models\qwen.gguf"></label>
                  <label class="field"><span>上下文长度</span><input v-model.number="pluginForm.LlamaCpp.ContextSize" type="number" min="512"></label>
                  <label class="field"><span>GPU 层数</span><input v-model.number="pluginForm.LlamaCpp.GpuLayers" type="number"></label>
                  <label class="field"><span>并行槽</span><input v-model.number="pluginForm.LlamaCpp.ParallelSlots" type="number" min="1"></label>
                  <label class="field"><span>Batch Size</span><input v-model.number="pluginForm.LlamaCpp.BatchSize" type="number" min="1"></label>
                  <label class="field"><span>UBatch Size</span><input v-model.number="pluginForm.LlamaCpp.UBatchSize" type="number" min="1"></label>
                  <label class="field"><span>Flash Attention</span><select v-model="pluginForm.LlamaCpp.FlashAttentionMode"><option value="auto">auto</option><option value="on">on</option><option value="off">off</option></select></label>
                </div>
              </section>

              <section class="panel">
                <h2><Wand2 class="section-icon" />贴图文字翻译</h2>
                <div class="checks">
                  <label class="check"><input v-model="pluginForm.TextureImageTranslation.Enabled" type="checkbox">启用贴图文字翻译</label>
                  <label class="check"><input v-model="pluginForm.TextureImageTranslation.EnableVisionConfirmation" type="checkbox">启用视觉确认</label>
                </div>
                <div class="field-grid four">
                  <label class="field"><span>服务地址</span><input v-model="pluginForm.TextureImageTranslation.BaseUrl"></label>
                  <label class="field"><span>编辑端点</span><input v-model="pluginForm.TextureImageTranslation.EditEndpoint"></label>
                  <label class="field"><span>视觉端点</span><input v-model="pluginForm.TextureImageTranslation.VisionEndpoint"></label>
                  <label class="field"><span>图片模型</span><input v-model="pluginForm.TextureImageTranslation.ImageModel"></label>
                  <label class="field"><span>视觉模型</span><input v-model="pluginForm.TextureImageTranslation.VisionModel"></label>
                  <label class="field"><span>质量</span><select v-model="pluginForm.TextureImageTranslation.Quality"><option value="low">low</option><option value="medium">medium</option><option value="high">high</option></select></label>
                  <label class="field"><span>超时秒数</span><input v-model.number="pluginForm.TextureImageTranslation.TimeoutSeconds" type="number" min="1"></label>
                  <label class="field"><span>并发数</span><input v-model.number="pluginForm.TextureImageTranslation.MaxConcurrentRequests" type="number" min="1"></label>
                </div>
              </section>
            </div>
          </template>
        </section>

        <section v-else-if="activePage === 'translations'" class="page translations-page">
          <div v-if="!selectedGame" class="empty-state">
            <Database class="empty-icon" />
            <strong>没有译文数据库目标</strong>
            <span>译文编辑会离线读取所选游戏目录里的 translation-cache.sqlite。</span>
            <button class="button-primary" type="button" @click="setPage('library')"><Gamepad2 class="icon" />打开游戏库</button>
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
          </template>
        </section>

        <section v-else class="page about-page">
          <div class="about-stage">
            <img class="about-watermark" :src="brandIcon" alt="" aria-hidden="true">
            <div class="about-brand-panel">
              <div class="about-logo-mark">
                <img :src="brandIcon" alt="HUnityAutoTranslator" width="128" height="128">
              </div>
              <div class="about-title-block">
                <span class="eyebrow">HUnityAutoTranslator</span>
                <h1>外部工具箱</h1>
                <p>面向本机游戏目录的安装、配置和离线译文维护工具。</p>
              </div>
              <div class="about-primary-version">
                <PackageCheck class="about-card-icon" />
                <span>工具箱版本</span>
                <strong>0.1.1</strong>
              </div>
            </div>
            <div class="about-detail-panel">
              <div class="about-detail-head">
                <span>Runtime Plate</span>
                <strong>环境铭牌</strong>
              </div>
              <dl class="about-version-list">
                <div class="about-version-row">
                  <dt><Gamepad2 class="about-card-icon" />当前游戏</dt>
                  <dd><strong>{{ selectedGame?.Name ?? "未选择" }}</strong><small>{{ selectedGame?.Root ?? "游戏库为空" }}</small></dd>
                </div>
                <div class="about-version-row">
                  <dt><MonitorCog class="about-card-icon" />运行时</dt>
                  <dd><strong>{{ runtimeSummary }}</strong></dd>
                </div>
                <div class="about-version-row">
                  <dt><Languages class="about-card-icon" />配置文件</dt>
                  <dd><strong>{{ pluginSettingsPath || "选择游戏后显示" }}</strong></dd>
                </div>
                <div class="about-version-row">
                  <dt><Database class="about-card-icon" />译文缓存</dt>
                  <dd><strong>{{ selectedGame ? `${selectedGame.Root}\\BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite` : "选择游戏后显示" }}</strong></dd>
                </div>
              </dl>
            </div>
          </div>
        </section>
      </main>
    </section>

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
  </div>
</template>
