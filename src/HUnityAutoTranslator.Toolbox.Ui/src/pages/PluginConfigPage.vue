<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import {
  CheckCircle2,
  FileText,
  Gamepad2,
  HardDrive,
  Keyboard,
  RotateCcw,
  Save,
  ScanLine,
  Settings,
  SlidersHorizontal,
  Timer,
  Type,
  Wand2
} from "lucide-vue-next";
import SectionPanel from "../components/SectionPanel.vue";
import { safeInvoke } from "../api/client";
import {
  requestNavigation,
  selectedGame as selectedGameFromStore,
  setDirtyForm,
  showToast,
  toolboxStore
} from "../state/toolboxStore";
import type {
  FontPickResult,
  PluginConfigPayload,
  PromptTemplateConfig,
  TextureImageTranslationConfig,
  TranslationQualityConfig,
  UpdateConfigRequest
} from "../types/api";

type HotkeyField = "OpenControlPanelHotkey" | "ToggleTranslationHotkey" | "ForceScanHotkey" | "ToggleFontHotkey";

const dirtyFormKey = "pluginConfig";
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

const pluginConfigBase = ref<UpdateConfigRequest>({});
const message = ref("选择游戏后读取插件配置。");
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

const selectedGame = computed(() => selectedGameFromStore());

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
  setDirtyForm(dirtyFormKey, false);
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

function markDirty(): void {
  setDirtyForm(dirtyFormKey, true, async () => {
    await saveConfig();
  });
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
    markDirty();
    return;
  }

  if (!normalizeCapturedKey(event.key)) {
    message.value = "不支持这个按键，请换一个主键。";
    showToast(message.value, "warn");
    return;
  }

  const hotkey = normalizeCapturedHotkey(event);
  if (!hotkey) {
    message.value = "需要使用 Ctrl、Shift 或 Alt 组合键。";
    showToast(message.value, "warn");
    return;
  }

  pluginForm[field] = hotkey;
  listeningHotkeyField.value = null;
  markDirty();
}

async function pickReplacementFontFile(): Promise<void> {
  if (isPickingFontFile.value) {
    return;
  }

  isPickingFontFile.value = true;
  try {
    const result = await safeInvoke<FontPickResult>("pickFontFile");
    if (!result) {
      return;
    }

    if (result.Status === "selected" && result.FilePath) {
      pluginForm.ReplacementFontFile = result.FilePath;
      pluginForm.ReplacementFontName = result.FontName ?? "";
      markDirty();
      message.value = result.FontName ? `已选择字体：${result.FontName}` : "已选择字体文件。";
      showToast(message.value, "ok");
      return;
    }

    if (result.Status !== "cancelled") {
      message.value = result.Message || "选择字体文件失败。";
      showToast(message.value, "warn");
    }
  } finally {
    isPickingFontFile.value = false;
  }
}

async function loadConfig(): Promise<void> {
  if (!selectedGame.value) {
    message.value = "请先在游戏库选择游戏。";
    return;
  }

  toolboxStore.isLoadingPluginConfig = true;
  try {
    const payload = await safeInvoke<PluginConfigPayload>("loadPluginConfig", { gameRoot: selectedGame.value.Root });
    if (!payload) {
      return;
    }
    pluginConfigBase.value = payload.Config ?? {};
    toolboxStore.pluginSettingsPath = payload.SettingsPath;
    toolboxStore.pluginConfig = payload.Config ?? {};
    applyPluginConfig(payload.Config);
    message.value = `已读取：${payload.SettingsPath}`;
  } finally {
    toolboxStore.isLoadingPluginConfig = false;
  }
}

async function saveConfig(): Promise<void> {
  if (!selectedGame.value) {
    message.value = "请先在游戏库选择游戏。";
    return;
  }

  toolboxStore.isSavingPluginConfig = true;
  try {
    const config = { ...pluginConfigBase.value, ...readPluginForm() };
    const payload = await safeInvoke<PluginConfigPayload>("savePluginConfig", {
      gameRoot: selectedGame.value.Root,
      config
    });
    if (!payload) {
      return;
    }
    pluginConfigBase.value = payload.Config ?? config;
    toolboxStore.pluginSettingsPath = payload.SettingsPath;
    toolboxStore.pluginConfig = pluginConfigBase.value;
    applyPluginConfig(pluginConfigBase.value);
    message.value = `已直接覆盖保存：${payload.SettingsPath}`;
    showToast("插件配置已保存。", "ok");
  } finally {
    toolboxStore.isSavingPluginConfig = false;
  }
}

watch(() => toolboxStore.selectedGameId, (id) => {
  if (id) {
    void loadConfig();
  }
});

onMounted(() => {
  if (selectedGame.value) {
    void loadConfig();
  }
});
</script>

<template>
  <section class="page config-page">
    <header class="page-hero">
      <div>
        <span class="eyebrow">{{ selectedGame ? selectedGame.Name : "未选择游戏" }}</span>
        <h1>插件配置</h1>
        <p>{{ toolboxStore.pluginSettingsPath || "选择游戏后读取插件 cfg 配置文件。" }}</p>
      </div>
      <div class="status-strip">
        <span class="pill">{{ toolboxStore.isLoadingPluginConfig ? "读取中" : toolboxStore.isSavingPluginConfig ? "保存中" : "就绪" }}</span>
        <span class="pill" v-if="toolboxStore.dirtyForms.has(dirtyFormKey)">有未保存修改</span>
      </div>
    </header>

    <div v-if="!selectedGame" class="panel empty-state">
      <Settings class="empty-icon" />
      <strong>没有配置目标</strong>
      <span>插件配置会直接覆盖所选游戏目录下的 cfg 文件。</span>
      <button class="button-primary" type="button" @click="requestNavigation('library')">
        <Gamepad2 class="icon" />打开游戏库
      </button>
    </div>

    <template v-else>
      <div class="config-toolbar">
        <button type="button" :disabled="toolboxStore.isLoadingPluginConfig" @click="loadConfig">
          <RotateCcw class="icon" />重新读取
        </button>
        <button class="button-primary" type="button" :disabled="toolboxStore.isSavingPluginConfig" @click="saveConfig">
          <Save class="icon" />{{ toolboxStore.isSavingPluginConfig ? "保存中" : "直接覆盖保存" }}
        </button>
      </div>
      <p class="message">{{ message }}</p>

      <div class="form-stack" @input="markDirty" @change="markDirty">
        <SectionPanel title="基础与性能" :icon="SlidersHorizontal">
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
        </SectionPanel>

        <SectionPanel title="运行与采集" :icon="ScanLine">
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
        </SectionPanel>

        <SectionPanel title="文本策略与上下文" :icon="FileText">
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
        </SectionPanel>

        <SectionPanel title="快捷键" :icon="Keyboard">
          <div class="field-grid four">
            <label class="field"><span>打开控制面板</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'OpenControlPanelHotkey' }" :value="hotkeyValue('OpenControlPanelHotkey')" readonly autocomplete="off" placeholder="Alt+H" @focus="beginHotkeyCapture('OpenControlPanelHotkey')" @click="beginHotkeyCapture('OpenControlPanelHotkey')" @blur="cancelHotkeyCapture('OpenControlPanelHotkey')" @keydown="handleHotkeyKeydown($event, 'OpenControlPanelHotkey')"></label>
            <label class="field"><span>原文/译文切换</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleTranslationHotkey' }" :value="hotkeyValue('ToggleTranslationHotkey')" readonly autocomplete="off" placeholder="Alt+F" @focus="beginHotkeyCapture('ToggleTranslationHotkey')" @click="beginHotkeyCapture('ToggleTranslationHotkey')" @blur="cancelHotkeyCapture('ToggleTranslationHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleTranslationHotkey')"></label>
            <label class="field"><span>全局扫描更新</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ForceScanHotkey' }" :value="hotkeyValue('ForceScanHotkey')" readonly autocomplete="off" placeholder="Alt+G" @focus="beginHotkeyCapture('ForceScanHotkey')" @click="beginHotkeyCapture('ForceScanHotkey')" @blur="cancelHotkeyCapture('ForceScanHotkey')" @keydown="handleHotkeyKeydown($event, 'ForceScanHotkey')"></label>
            <label class="field"><span>字体状态切换</span><input class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleFontHotkey' }" :value="hotkeyValue('ToggleFontHotkey')" readonly autocomplete="off" placeholder="Alt+D" @focus="beginHotkeyCapture('ToggleFontHotkey')" @click="beginHotkeyCapture('ToggleFontHotkey')" @blur="cancelHotkeyCapture('ToggleFontHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleFontHotkey')"></label>
          </div>
        </SectionPanel>

        <SectionPanel title="提示词模板覆盖" :icon="FileText">
          <div class="field-grid two">
            <label v-for="field in promptTemplateFields" :key="field.key" class="field">
              <span>{{ field.label }}</span>
              <textarea v-model="pluginForm.PromptTemplates[field.key]" :placeholder="field.placeholder" spellcheck="false"></textarea>
            </label>
          </div>
        </SectionPanel>

        <SectionPanel title="译文质量检查" :icon="CheckCircle2">
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
        </SectionPanel>

        <SectionPanel title="字体补字 fallback" :icon="Type">
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
            <div class="field wide">
              <span>手动字体文件</span>
              <div class="input-action-row">
                <input v-model="pluginForm.ReplacementFontFile" placeholder="TTF/OTF 路径">
                <button type="button" :disabled="isPickingFontFile" @click="pickReplacementFontFile">
                  <FileText class="icon" />{{ isPickingFontFile ? "选择中" : "选择字体" }}
                </button>
              </div>
            </div>
            <label class="field"><span>字体采样字号</span><input v-model.number="pluginForm.FontSamplingPointSize" type="number" min="16"></label>
            <label class="field"><span>字号调整方式</span><select v-model="pluginForm.FontSizeAdjustmentMode"><option value="Disabled">关闭</option><option value="Percent">百分比例</option><option value="Points">固定增减</option></select></label>
            <label class="field"><span>字号调整值</span><input v-model.number="pluginForm.FontSizeAdjustmentValue" type="number" step="0.1"></label>
          </div>
        </SectionPanel>

        <SectionPanel title="llama.cpp" :icon="HardDrive">
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
        </SectionPanel>

        <SectionPanel title="贴图文字翻译" :icon="Wand2">
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
        </SectionPanel>
      </div>
    </template>
  </section>
</template>
