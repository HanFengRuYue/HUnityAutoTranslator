<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import {
  ArrowDown,
  ArrowUp,
  Bot,
  Brain,
  Clock3,
  Download,
  FileInput,
  FileOutput,
  FolderOpen,
  Gauge,
  Gamepad2,
  KeyRound,
  Layers,
  ListChecks,
  MessageSquareText,
  Play,
  Plus,
  RotateCcw,
  Save,
  Server,
  Settings2,
  Square,
  Thermometer,
  Trash2,
  WalletCards,
  X,
  Zap
} from "lucide-vue-next";
import { api, getText } from "../api/client";
import SectionPanel from "../components/SectionPanel.vue";
import {
  controlPanelStore,
  refreshState,
  saveConfig,
  setDirtyForm,
  showToast
} from "../state/controlPanelStore";
import type {
  ControlPanelState,
  LlamaCppBenchmarkCandidate,
  LlamaCppBenchmarkResult,
  LlamaCppConfig,
  LlamaCppModelDownloadPreset,
  LlamaCppModelDownloadRequest,
  LlamaCppModelDownloadStatus,
  LlamaCppModelPickResult,
  LlamaCppServerStatus,
  PromptTemplateConfig,
  ProviderBalanceInfo,
  ProviderBalanceResult,
  ProviderModelsResult,
  ProviderProfileImportResult,
  ProviderProfileState,
  ProviderProfileUpdateRequest,
  ProviderTestResult,
  UpdateConfigRequest
} from "../types/api";

const formKey = "ai";
type PromptTemplateKey = keyof PromptTemplateConfig;

interface SaveBehavior {
  quiet?: boolean;
}

interface ProviderProfileSaveBehavior {
  closeEditor?: boolean;
}

const providerKindOptions = [
  { value: 0, label: "OpenAI" },
  { value: 1, label: "DeepSeek" },
  { value: 2, label: "OpenAI 兼容" },
  { value: 3, label: "llama.cpp 本地模型" }
];

const providerDefaults: Record<number, { name: string; baseUrl: string; endpoint: string; model: string }> = {
  0: { name: "OpenAI", baseUrl: "https://api.openai.com", endpoint: "/v1/responses", model: "gpt-5.5" },
  1: { name: "DeepSeek", baseUrl: "https://api.deepseek.com", endpoint: "/chat/completions", model: "deepseek-v4-flash" },
  2: { name: "OpenAI 兼容", baseUrl: "http://127.0.0.1:8000", endpoint: "/v1/chat/completions", model: "local-model" },
  3: { name: "llama.cpp 本地模型", baseUrl: "http://127.0.0.1:0", endpoint: "/v1/chat/completions", model: "local-model" }
};

const llamaCppStateLabels: Record<string, string> = {
  stopped: "已停止",
  starting: "启动中",
  running: "运行中",
  error: "错误"
};

const styleHints: Record<number, string> = {
  0: "忠实：尽量保留原文含义和语气。",
  1: "自然：优先输出流畅中文。",
  2: "本地化：适合游戏语境和 UI 文案。",
  3: "简短：菜单、按钮和提示更短。"
};

const targetLanguageOptions = [
  { value: "zh-Hans", label: "简体中文" },
  { value: "zh-Hant", label: "繁体中文" },
  { value: "en", label: "英语" },
  { value: "ja", label: "日语" },
  { value: "ko", label: "韩语" },
  { value: "fr", label: "法语" },
  { value: "de", label: "德语" },
  { value: "es", label: "西班牙语" },
  { value: "pt", label: "葡萄牙语" },
  { value: "pt-BR", label: "巴西葡萄牙语" },
  { value: "ru", label: "俄语" },
  { value: "it", label: "意大利语" },
  { value: "th", label: "泰语" },
  { value: "vi", label: "越南语" },
  { value: "id", label: "印尼语" },
  { value: "tr", label: "土耳其语" },
  { value: "ar", label: "阿拉伯语" }
];

const promptTemplateFields: Array<{
  key: PromptTemplateKey;
  label: string;
  help: string;
  placeholders: string[];
  required: string[];
}> = [
  { key: "SystemPrompt", label: "系统提示词", help: "控制模型的总体翻译规则、目标语言、游戏背景和术语策略。", placeholders: ["{TargetLanguage}", "{StyleInstruction}", "{GameTitle}", "{GameContext}", "{GlossarySystemPolicy}"], required: [] },
  { key: "GlossarySystemPolicy", label: "术语库约束", help: "告诉模型术语库规则必须优先。", placeholders: [], required: [] },
  { key: "BatchUserPrompt", label: "批量翻译请求", help: "定义每批文本如何发给模型，必须保留输入 JSON 占位符。", placeholders: ["{PromptSections}", "{InputJson}"], required: ["{InputJson}"] },
  { key: "GlossaryTermsSection", label: "术语条目片段", help: "把命中的术语注入提示词。", placeholders: ["{GlossaryTermsJson}"], required: ["{GlossaryTermsJson}"] },
  { key: "CurrentItemContextSection", label: "当前上下文片段", help: "把场景、层级和邻近标签发给模型。", placeholders: ["{ItemContextsJson}"], required: ["{ItemContextsJson}"] },
  { key: "ItemHintsSection", label: "短文本提示片段", help: "为按钮、开关、设置值等短 UI 文本提供角色提示。", placeholders: ["{ItemHintsJson}"], required: ["{ItemHintsJson}"] },
  { key: "ContextExamplesSection", label: "历史上下文片段", help: "提供同组件历史译文作为风格参考。", placeholders: ["{ContextExamplesJson}"], required: ["{ContextExamplesJson}"] },
  { key: "GlossaryRepairPrompt", label: "术语修复提示词", help: "当译文漏用强制术语时进行单条修复。", placeholders: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RequiredGlossaryTermsJson}", "{RequiredGlossaryTermsBlock}"], required: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}"] },
  { key: "QualityRepairPrompt", label: "质量修复提示词", help: "当格式、长度或语义校验失败时二次修复。", placeholders: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RepairContextJson}", "{GameTitle}"], required: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RepairContextJson}"] },
  { key: "GlossaryExtractionSystemPrompt", label: "术语抽取系统", help: "控制自动术语抽取时模型扮演的角色。", placeholders: [], required: [] },
  { key: "GlossaryExtractionUserPrompt", label: "术语抽取请求", help: "定义待分析文本如何发给模型。", placeholders: ["{RowsJson}"], required: ["{RowsJson}"] }
];

function createPromptTemplates(): PromptTemplateConfig {
  return {
    SystemPrompt: null,
    GlossarySystemPolicy: null,
    BatchUserPrompt: null,
    GlossaryTermsSection: null,
    CurrentItemContextSection: null,
    ItemHintsSection: null,
    ContextExamplesSection: null,
    GlossaryRepairPrompt: null,
    QualityRepairPrompt: null,
    GlossaryExtractionSystemPrompt: null,
    GlossaryExtractionUserPrompt: null
  };
}

const form = reactive({
  TargetLanguage: "zh-Hans",
  GameTitle: "",
  Style: 2,
  ProviderKind: 0,
  MaxBatchCharacters: 1800,
  EnableTranslationContext: true,
  TranslationContextMaxExamples: 4,
  TranslationContextMaxCharacters: 1200,
  EnableGlossary: true,
  EnableAutoTermExtraction: false,
  GlossaryMaxTerms: 16,
  GlossaryMaxCharacters: 1200,
  CustomPrompt: "",
  PromptTemplates: createPromptTemplates(),
  LlamaCppModelPath: "",
  LlamaCppContextSize: 4096,
  LlamaCppGpuLayers: 999,
  LlamaCppParallelSlots: 1,
  LlamaCppBatchSize: 2048,
  LlamaCppUBatchSize: 512,
  LlamaCppFlashAttentionMode: "auto",
  LlamaCppAutoStartOnStartup: false
});

const profileForm = reactive({
  Id: "",
  Name: "",
  Enabled: true,
  Kind: 0,
  BaseUrl: "",
  Endpoint: "",
  Model: "",
  ApiKey: "",
  ClearApiKey: false,
  MaxConcurrentRequests: 4,
  RequestsPerMinute: 60,
  RequestTimeoutSeconds: 30,
  ReasoningEffort: "none",
  OutputVerbosity: "low",
  DeepSeekThinkingMode: "disabled",
  Temperature: "",
  OpenAICompatibleCustomHeaders: "",
  OpenAICompatibleExtraBodyJson: "",
  LlamaCppModelPath: "",
  LlamaCppContextSize: 4096,
  LlamaCppGpuLayers: 999,
  LlamaCppParallelSlots: 1,
  LlamaCppBatchSize: 2048,
  LlamaCppUBatchSize: 512,
  LlamaCppFlashAttentionMode: "auto",
  LlamaCppAutoStartOnStartup: false
});

const selectedProfileId = ref("");
const providerEditorOpen = ref(false);
const profileDirty = ref(false);
const profileBusy = ref(false);
const utilityBusy = ref(false);
const importInput = ref<HTMLInputElement | null>(null);
const llamaCppBusy = ref(false);
const llamaCppModelPicking = ref(false);
const llamaCppBenchmarkBusy = ref(false);
const llamaCppBenchmarkResult = ref<LlamaCppBenchmarkResult | null>(null);
const llamaCppModelPresets = ref<LlamaCppModelDownloadPreset[]>([]);
const llamaCppSelectedPresetId = ref("");
const llamaCppDownloadDialogOpen = ref(false);
const llamaCppDownloadStatus = ref<LlamaCppModelDownloadStatus | null>(null);
const llamaCppCompletedPath = ref<string | null>(null);
const activePromptTemplateKey = ref<PromptTemplateKey>("SystemPrompt");

const formDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));
const providerProfiles = computed(() => controlPanelStore.state?.ProviderProfiles ?? []);
const selectedProfile = computed(() => providerProfiles.value.find((profile) => profile.Id === selectedProfileId.value) ?? null);
const activeProviderProfileName = computed(() => controlPanelStore.state?.ActiveProviderProfileName ?? "无可用配置");
const hasLlamaCppProfile = computed(() => providerProfiles.value.some((profile) => providerKindToNumber(profile.Kind) === 3));
const profileKind = computed(() => numberValue(profileForm.Kind));
const isProfileOpenAi = computed(() => profileKind.value === 0);
const isProfileDeepSeek = computed(() => profileKind.value === 1);
const isProfileOpenAiCompatible = computed(() => profileKind.value === 2);
const isProfileLlamaCpp = computed(() => profileKind.value === 3);
const activeStyleHint = computed(() => styleHints[numberValue(form.Style)] ?? "");
const automaticGameTitle = computed(() => controlPanelStore.state?.AutomaticGameTitle ?? "");
const defaultPromptTemplates = computed(() => controlPanelStore.state?.DefaultPromptTemplates ?? createPromptTemplates());
const activePromptTemplate = computed(() =>
  promptTemplateFields.find((field) => field.key === activePromptTemplateKey.value) ?? promptTemplateFields[0]);
const activePromptTemplateText = computed({
  get: () => form.PromptTemplates[activePromptTemplateKey.value] ?? "",
  set: (value: string) => {
    form.PromptTemplates[activePromptTemplateKey.value] = value;
    markDirty();
  }
});
const promptUsesDefault = computed(() => promptTemplateFields.every((field) => promptTemplateUsesDefault(field.key)));
const promptModeText = computed(() => promptUsesDefault.value ? "正在使用内置提示词" : "正在使用自定义提示词");
const llamaCppStatus = computed(() => controlPanelStore.state?.LlamaCppStatus);
const llamaCppStateText = computed(() => {
  const state = (llamaCppStatus.value?.State ?? "stopped").toLowerCase();
  return llamaCppStateLabels[state] ?? state;
});
const llamaCppIsActive = computed(() => {
  const state = (llamaCppStatus.value?.State ?? "").toLowerCase();
  return state === "starting" || state === "running";
});
const llamaCppStatusText = computed(() => llamaCppStatus.value?.Message ?? "本地模型未启动。");
const llamaCppInstallText = computed(() => {
  const status = llamaCppStatus.value;
  if (!status?.Installed) {
    return "未检测到插件内 llama.cpp";
  }

  const release = status.Release ? ` ${status.Release}` : "";
  const variant = status.Variant || status.Backend || "unknown";
  return `${variant}${release}`;
});
const selectedLlamaCppPreset = computed(() =>
  llamaCppModelPresets.value.find((preset) => preset.Id === llamaCppSelectedPresetId.value) ?? llamaCppModelPresets.value[0] ?? null);
const isLlamaCppDownloading = computed(() => llamaCppDownloadStatus.value?.State === "downloading");
const llamaCppDownloadProgressPercent = computed(() =>
  Math.max(0, Math.min(100, llamaCppDownloadStatus.value?.ProgressPercent ?? 0)));
const llamaCppDownloadText = computed(() => {
  const status = llamaCppDownloadStatus.value;
  if (!status) {
    return "尚未开始下载。";
  }

  const size = status.TotalBytes > 0
    ? `${formatBytes(status.DownloadedBytes)} / ${formatBytes(status.TotalBytes)}`
    : formatBytes(status.DownloadedBytes);
  return `${size} · 速度 ${formatLlamaCppDownloadSpeed(status)} · 剩余 ${formatLlamaCppRemainingTime(status)}`;
});

function markDirty(): void {
  setDirtyForm(formKey, true);
}

function markProfileDirty(): void {
  profileDirty.value = true;
}

function numberValue(value: number | string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function formatBytes(value: number | null | undefined): string {
  const bytes = Math.max(0, Number(value ?? 0));
  if (bytes >= 1024 * 1024 * 1024) {
    return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GiB`;
  }

  if (bytes >= 1024 * 1024) {
    return `${(bytes / 1024 / 1024).toFixed(1)} MiB`;
  }

  return `${Math.round(bytes / 1024)} KiB`;
}

function calculateLlamaCppDownloadSpeed(status: LlamaCppModelDownloadStatus): number | null {
  const started = status.StartedUtc ? Date.parse(status.StartedUtc) : Number.NaN;
  const downloaded = Math.max(0, Number(status.DownloadedBytes ?? 0));
  if (!Number.isFinite(started) || downloaded <= 0) {
    return null;
  }

  const elapsedSeconds = (Date.now() - started) / 1000;
  if (!Number.isFinite(elapsedSeconds) || elapsedSeconds <= 0.25) {
    return null;
  }

  const speed = downloaded / elapsedSeconds;
  return Number.isFinite(speed) && speed > 0 ? speed : null;
}

function formatLlamaCppDownloadSpeed(status: LlamaCppModelDownloadStatus): string {
  const speed = calculateLlamaCppDownloadSpeed(status);
  if (!speed) {
    return "计算中";
  }

  if (speed < 1024) {
    return `${Math.max(1, Math.round(speed))} B/s`;
  }

  return `${formatBytes(speed)}/s`;
}

function formatLlamaCppRemainingTime(status: LlamaCppModelDownloadStatus): string {
  const total = Math.max(0, Number(status.TotalBytes ?? 0));
  const downloaded = Math.max(0, Number(status.DownloadedBytes ?? 0));
  if (total <= 0 || downloaded <= 0) {
    return "计算中";
  }

  const remainingBytes = Math.max(0, total - downloaded);
  if (remainingBytes <= 0) {
    return "0秒";
  }

  const speed = calculateLlamaCppDownloadSpeed(status);
  if (!speed) {
    return "计算中";
  }

  return formatDurationSeconds(remainingBytes / speed);
}

function formatDurationSeconds(value: number): string {
  const seconds = Math.max(1, Math.ceil(value));
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  const remainingSeconds = seconds % 60;
  if (hours > 0) {
    return `${hours}小时${minutes}分`;
  }

  if (minutes > 0) {
    return `${minutes}分${remainingSeconds}秒`;
  }

  return `${remainingSeconds}秒`;
}

function providerKindToNumber(value: number | string | null | undefined): number {
  if (typeof value === "number") {
    return value;
  }

  const normalized = String(value ?? "").toLowerCase();
  if (normalized === "deepseek" || normalized === "1") {
    return 1;
  }

  if (normalized === "openaicompatible" || normalized === "2") {
    return 2;
  }

  if (normalized === "llamacpp" || normalized === "3") {
    return 3;
  }

  return 0;
}

function formatProviderKind(value: number | string): string {
  const kind = providerKindToNumber(value);
  return providerKindOptions.find((item) => item.value === kind)?.label ?? "OpenAI";
}

function formatProfileStatus(profile: ProviderProfileState): string {
  if (profile.CooldownRemainingSeconds > 0) {
    return `冷却 ${profile.CooldownRemainingSeconds}s`;
  }

  if (providerKindToNumber(profile.Kind) === 3 && !profile.LlamaCpp?.ModelPath) {
    return "缺少模型";
  }

  if (profile.IsActive) {
    return "当前";
  }

  if (!profile.Enabled) {
    return "停用";
  }

  if (!profile.ApiKeyConfigured && providerKindToNumber(profile.Kind) !== 2 && providerKindToNumber(profile.Kind) !== 3) {
    return "缺少 Key";
  }

  return "待命";
}

function normalizePrompt(value: string | null | undefined): string {
  return (value ?? "").replace(/\r\n/g, "\n").trim();
}

function promptTemplateUsesDefault(key: PromptTemplateKey): boolean {
  return normalizePrompt(form.PromptTemplates[key]) === normalizePrompt(defaultPromptTemplates.value[key]);
}

function applyPromptTemplates(overrides: PromptTemplateConfig | null | undefined, defaults: PromptTemplateConfig | null | undefined): void {
  const next = createPromptTemplates();
  const defaultValues = defaults ?? createPromptTemplates();
  for (const field of promptTemplateFields) {
    next[field.key] = overrides?.[field.key] ?? defaultValues[field.key] ?? "";
  }

  form.PromptTemplates = next;
  form.CustomPrompt = next.SystemPrompt ?? "";
}

function buildPromptTemplateOverrides(): PromptTemplateConfig {
  const overrides = createPromptTemplates();
  for (const field of promptTemplateFields) {
    const value = form.PromptTemplates[field.key] ?? "";
    overrides[field.key] = normalizePrompt(value) === normalizePrompt(defaultPromptTemplates.value[field.key])
      ? null
      : value;
  }

  return overrides;
}

function validatePromptTemplates(): boolean {
  for (const field of promptTemplateFields) {
    const value = form.PromptTemplates[field.key] ?? "";
    for (const placeholder of field.required) {
      if (!value.includes(placeholder)) {
        activePromptTemplateKey.value = field.key;
        showToast(`${field.label} 必须保留 ${placeholder} 占位符。`, "warn", 5200);
        return false;
      }
    }
  }

  return true;
}

function applyState(state: ControlPanelState | null, force = false): void {
  if (!state || (!force && formDirty.value)) {
    return;
  }

  form.TargetLanguage = state.TargetLanguage ?? "zh-Hans";
  form.GameTitle = state.GameTitle ?? "";
  form.Style = numberValue(state.Style);
  form.ProviderKind = providerKindToNumber(state.ProviderKind);
  form.MaxBatchCharacters = state.MaxBatchCharacters ?? 1800;
  form.EnableTranslationContext = Boolean(state.EnableTranslationContext);
  form.TranslationContextMaxExamples = state.TranslationContextMaxExamples ?? 4;
  form.TranslationContextMaxCharacters = state.TranslationContextMaxCharacters ?? 1200;
  form.EnableGlossary = Boolean(state.EnableGlossary);
  form.EnableAutoTermExtraction = Boolean(state.EnableAutoTermExtraction);
  form.GlossaryMaxTerms = state.GlossaryMaxTerms ?? 16;
  form.GlossaryMaxCharacters = state.GlossaryMaxCharacters ?? 1200;
  form.LlamaCppModelPath = state.LlamaCpp?.ModelPath ?? "";
  form.LlamaCppContextSize = state.LlamaCpp?.ContextSize ?? 4096;
  form.LlamaCppGpuLayers = state.LlamaCpp?.GpuLayers ?? 999;
  form.LlamaCppParallelSlots = state.LlamaCpp?.ParallelSlots ?? 1;
  form.LlamaCppBatchSize = state.LlamaCpp?.BatchSize ?? 2048;
  form.LlamaCppUBatchSize = state.LlamaCpp?.UBatchSize ?? 512;
  form.LlamaCppFlashAttentionMode = state.LlamaCpp?.FlashAttentionMode ?? "auto";
  form.LlamaCppAutoStartOnStartup = state.LlamaCpp?.AutoStartOnStartup ?? false;
  applyPromptTemplates(state.PromptTemplates, state.DefaultPromptTemplates);
  setDirtyForm(formKey, false);
}

function applySelectedProfile(force = false): void {
  const profile = selectedProfile.value;
  if (!profile || (!force && profileDirty.value)) {
    return;
  }

  profileForm.Id = profile.Id;
  profileForm.Name = profile.Name;
  profileForm.Enabled = profile.Enabled;
  profileForm.Kind = providerKindToNumber(profile.Kind);
  profileForm.BaseUrl = profile.BaseUrl;
  profileForm.Endpoint = profile.Endpoint;
  profileForm.Model = profile.Model;
  profileForm.ApiKey = "";
  profileForm.ClearApiKey = false;
  profileForm.MaxConcurrentRequests = profile.MaxConcurrentRequests;
  profileForm.RequestsPerMinute = profile.RequestsPerMinute;
  profileForm.RequestTimeoutSeconds = profile.RequestTimeoutSeconds;
  profileForm.ReasoningEffort = profile.ReasoningEffort;
  profileForm.OutputVerbosity = profile.OutputVerbosity;
  profileForm.DeepSeekThinkingMode = profile.DeepSeekThinkingMode;
  profileForm.Temperature = profile.Temperature == null ? "" : String(profile.Temperature);
  profileForm.OpenAICompatibleCustomHeaders = profile.OpenAICompatibleCustomHeaders ?? "";
  profileForm.OpenAICompatibleExtraBodyJson = profile.OpenAICompatibleExtraBodyJson ?? "";
  applyProfileLlamaCppConfig(profile.LlamaCpp);
  profileDirty.value = false;
}

function selectFirstProfileIfNeeded(): void {
  if (selectedProfileId.value && providerProfiles.value.some((profile) => profile.Id === selectedProfileId.value)) {
    return;
  }

  selectedProfileId.value = providerProfiles.value.find((profile) => profile.IsActive)?.Id ?? providerProfiles.value[0]?.Id ?? "";
  applySelectedProfile(true);
}

function buildConfigRequest(providerKind = form.ProviderKind): UpdateConfigRequest {
  return {
    TargetLanguage: form.TargetLanguage,
    GameTitle: form.GameTitle.trim() || null,
    Style: numberValue(form.Style),
    ProviderKind: providerKind,
    MaxBatchCharacters: numberValue(form.MaxBatchCharacters),
    EnableTranslationContext: form.EnableTranslationContext,
    TranslationContextMaxExamples: numberValue(form.TranslationContextMaxExamples),
    TranslationContextMaxCharacters: numberValue(form.TranslationContextMaxCharacters),
    EnableGlossary: form.EnableGlossary,
    EnableAutoTermExtraction: form.EnableAutoTermExtraction,
    GlossaryMaxTerms: numberValue(form.GlossaryMaxTerms),
    GlossaryMaxCharacters: numberValue(form.GlossaryMaxCharacters),
    PromptTemplates: buildPromptTemplateOverrides(),
    LlamaCpp: buildLlamaCppConfig()
  };
}

function buildLlamaCppConfig(): LlamaCppConfig {
  return {
    ModelPath: form.LlamaCppModelPath.trim() || null,
    ContextSize: numberValue(form.LlamaCppContextSize),
    GpuLayers: numberValue(form.LlamaCppGpuLayers),
    ParallelSlots: numberValue(form.LlamaCppParallelSlots),
    BatchSize: numberValue(form.LlamaCppBatchSize),
    UBatchSize: numberValue(form.LlamaCppUBatchSize),
    FlashAttentionMode: form.LlamaCppFlashAttentionMode,
    AutoStartOnStartup: form.LlamaCppAutoStartOnStartup
  };
}

function buildProfileLlamaCppConfig(): LlamaCppConfig {
  return {
    ModelPath: profileForm.LlamaCppModelPath.trim() || null,
    ContextSize: numberValue(profileForm.LlamaCppContextSize),
    GpuLayers: numberValue(profileForm.LlamaCppGpuLayers),
    ParallelSlots: numberValue(profileForm.LlamaCppParallelSlots),
    BatchSize: numberValue(profileForm.LlamaCppBatchSize),
    UBatchSize: numberValue(profileForm.LlamaCppUBatchSize),
    FlashAttentionMode: profileForm.LlamaCppFlashAttentionMode,
    AutoStartOnStartup: profileForm.LlamaCppAutoStartOnStartup
  };
}

function buildProviderProfileRequest(): ProviderProfileUpdateRequest {
  return {
    Name: profileForm.Name.trim() || (providerDefaults[profileKind.value]?.name ?? "服务商配置"),
    Enabled: profileForm.Enabled,
    Kind: profileKind.value,
    BaseUrl: profileForm.BaseUrl.trim(),
    Endpoint: profileForm.Endpoint.trim(),
    Model: profileForm.Model.trim(),
    ApiKey: profileForm.ApiKey.trim() || null,
    ClearApiKey: profileForm.ClearApiKey,
    MaxConcurrentRequests: numberValue(profileForm.MaxConcurrentRequests),
    RequestsPerMinute: numberValue(profileForm.RequestsPerMinute),
    RequestTimeoutSeconds: numberValue(profileForm.RequestTimeoutSeconds),
    ReasoningEffort: profileForm.ReasoningEffort,
    OutputVerbosity: profileForm.OutputVerbosity,
    DeepSeekThinkingMode: profileForm.DeepSeekThinkingMode,
    Temperature: profileForm.Temperature === "" ? null : Number(profileForm.Temperature),
    ClearTemperature: profileForm.Temperature === "",
    OpenAICompatibleCustomHeaders: profileForm.OpenAICompatibleCustomHeaders.trim() || null,
    OpenAICompatibleExtraBodyJson: profileForm.OpenAICompatibleExtraBodyJson.trim() || null,
    LlamaCpp: isProfileLlamaCpp.value ? buildProfileLlamaCppConfig() : null
  };
}

function applyProfileLlamaCppConfig(config: LlamaCppConfig | null | undefined): void {
  const defaults = config ?? {
    ModelPath: null,
    ContextSize: 4096,
    GpuLayers: 999,
    ParallelSlots: 1,
    BatchSize: 2048,
    UBatchSize: 512,
    FlashAttentionMode: "auto",
    AutoStartOnStartup: false
  };
  profileForm.LlamaCppModelPath = defaults.ModelPath ?? "";
  profileForm.LlamaCppContextSize = defaults.ContextSize;
  profileForm.LlamaCppGpuLayers = defaults.GpuLayers;
  profileForm.LlamaCppParallelSlots = defaults.ParallelSlots;
  profileForm.LlamaCppBatchSize = defaults.BatchSize;
  profileForm.LlamaCppUBatchSize = defaults.UBatchSize;
  profileForm.LlamaCppFlashAttentionMode = defaults.FlashAttentionMode;
  profileForm.LlamaCppAutoStartOnStartup = defaults.AutoStartOnStartup;
}

async function saveGlobalConfig(options: SaveBehavior = {}): Promise<void> {
  if (!validatePromptTemplates()) {
    return;
  }

  const state = await saveConfig(buildConfigRequest(), formKey, options);
  if (state) {
    applyState(state, true);
  }
}

async function saveUseOnlineProfiles(): Promise<void> {
  form.ProviderKind = 0;
  const state = await saveConfig(buildConfigRequest(0), formKey, { quiet: true });
  if (state) {
    applyState(state, true);
    showToast("已切换为按服务商配置优先级翻译。", "ok");
  }
}

function createProfileDefaults(kind = 0): boolean {
  if (kind === 3 && hasLlamaCppProfile.value) {
    showToast("只能创建一个本地模型配置。", "warn");
    return false;
  }

  const defaults = providerDefaults[kind] ?? providerDefaults[0];
  profileForm.Id = "";
  profileForm.Name = defaults.name;
  profileForm.Enabled = true;
  profileForm.Kind = kind;
  profileForm.BaseUrl = defaults.baseUrl;
  profileForm.Endpoint = defaults.endpoint;
  profileForm.Model = defaults.model;
  profileForm.ApiKey = "";
  profileForm.ClearApiKey = false;
  profileForm.MaxConcurrentRequests = 4;
  profileForm.RequestsPerMinute = 60;
  profileForm.RequestTimeoutSeconds = 30;
  profileForm.ReasoningEffort = "none";
  profileForm.OutputVerbosity = "low";
  profileForm.DeepSeekThinkingMode = "disabled";
  profileForm.Temperature = "";
  profileForm.OpenAICompatibleCustomHeaders = "";
  profileForm.OpenAICompatibleExtraBodyJson = "";
  applyProfileLlamaCppConfig(null);
  selectedProfileId.value = "";
  profileDirty.value = true;
  return true;
}

function openNewProviderProfile(kind = 0): void {
  if (createProfileDefaults(kind)) {
    providerEditorOpen.value = true;
  }
}

function openProviderProfileEditor(profile: ProviderProfileState): void {
  selectedProfileId.value = profile.Id;
  applySelectedProfile(true);
  providerEditorOpen.value = true;
}

function closeProviderProfileEditor(): void {
  if (profileDirty.value && !window.confirm("放弃未保存的服务商配置修改？")) {
    return;
  }

  if (selectedProfileId.value) {
    applySelectedProfile(true);
  } else {
    profileDirty.value = false;
  }

  providerEditorOpen.value = false;
}

async function createLlamaCppProfile(): Promise<void> {
  openNewProviderProfile(3);
}

function applyProfileKindDefaults(): void {
  if (profileKind.value === 3 && hasLlamaCppProfile.value && selectedProfile.value?.Id !== profileForm.Id) {
    showToast("只能创建一个本地模型配置。", "warn");
    profileForm.Kind = providerKindToNumber(selectedProfile.value?.Kind ?? 0);
    return;
  }

  const currentId = profileForm.Id;
  const currentName = profileForm.Name;
  const defaults = providerDefaults[profileKind.value] ?? providerDefaults[0];
  profileForm.BaseUrl = defaults.baseUrl;
  profileForm.Endpoint = defaults.endpoint;
  profileForm.Model = defaults.model;
  profileForm.Name = currentName || defaults.name;
  profileForm.Id = currentId;
  if (profileKind.value === 3) {
    applyProfileLlamaCppConfig(null);
  }
  markProfileDirty();
}

async function createProviderProfile(options: ProviderProfileSaveBehavior = {}): Promise<void> {
  profileBusy.value = true;
  try {
    const state = await api<ControlPanelState>("/api/provider-profiles", { method: "POST", body: buildProviderProfileRequest() });
    controlPanelStore.state = state;
    selectFirstProfileIfNeeded();
    const profiles = state.ProviderProfiles ?? [];
    selectedProfileId.value = profiles.length ? profiles[profiles.length - 1].Id : selectedProfileId.value;
    applySelectedProfile(true);
    if (options.closeEditor) {
      providerEditorOpen.value = false;
    }
    showToast("服务商配置已添加。", "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "添加服务商配置失败", "error");
  } finally {
    profileBusy.value = false;
  }
}

async function saveProviderProfile(options: ProviderProfileSaveBehavior = {}): Promise<void> {
  if (!profileForm.Id) {
    await createProviderProfile(options);
    return;
  }

  profileBusy.value = true;
  try {
    const state = await api<ControlPanelState>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}`, {
      method: "PUT",
      body: buildProviderProfileRequest()
    });
    controlPanelStore.state = state;
    applySelectedProfile(true);
    if (options.closeEditor) {
      providerEditorOpen.value = false;
    }
    showToast("服务商配置已保存。", "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "保存服务商配置失败", "error");
  } finally {
    profileBusy.value = false;
  }
}

async function deleteProviderProfile(profile: ProviderProfileState): Promise<void> {
  if (!window.confirm(`删除服务商配置“${profile.Name}”？`)) {
    return;
  }

  try {
    await api<{ DeletedCount: number }>(`/api/provider-profiles/${encodeURIComponent(profile.Id)}`, { method: "DELETE" });
    await refreshState({ quiet: true });
    selectFirstProfileIfNeeded();
    if (profile.Id === profileForm.Id) {
      providerEditorOpen.value = false;
    }
    showToast("服务商配置已删除。", "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "删除服务商配置失败", "error");
  }
}

async function moveProviderProfile(profile: ProviderProfileState, direction: -1 | 1): Promise<void> {
  await api<ControlPanelState>(`/api/provider-profiles/${encodeURIComponent(profile.Id)}/${direction < 0 ? "move-up" : "move-down"}`, { method: "POST" })
    .then((state) => {
      controlPanelStore.state = state;
      selectedProfileId.value = profile.Id;
      applySelectedProfile(true);
    })
    .catch((error) => showToast(error instanceof Error ? error.message : "调整优先级失败", "error"));
}

async function exportProviderProfile(profile: ProviderProfileState): Promise<void> {
  try {
    const content = await getText(`/api/provider-profiles/${encodeURIComponent(profile.Id)}/export`);
    const blob = new Blob([content], { type: "application/octet-stream" });
    const anchor = document.createElement("a");
    anchor.href = URL.createObjectURL(blob);
    anchor.download = `${profile.Name || "provider"}.hutprovider`;
    anchor.click();
    URL.revokeObjectURL(anchor.href);
  } catch (error) {
    showToast(error instanceof Error ? error.message : "导出服务商配置失败", "error");
  }
}

function openImportPicker(): void {
  importInput.value?.click();
}

async function importProviderProfile(event: Event): Promise<void> {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  input.value = "";
  if (!file) {
    return;
  }

  try {
    const result = await api<ProviderProfileImportResult>("/api/provider-profiles/import", {
      method: "POST",
      body: await file.text()
    });
    await refreshState({ quiet: true });
    if (result.Profile) {
      selectedProfileId.value = result.Profile.Id;
      applySelectedProfile(true);
    }

    showToast(result.Message, result.Succeeded ? "ok" : "error");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "导入服务商配置失败", "error");
  }
}

async function runProfileUtility<T>(label: string, action: () => Promise<T>, render: (result: T) => string): Promise<void> {
  if (!profileForm.Id) {
    showToast("请先保存服务商配置。", "warn");
    return;
  }

  utilityBusy.value = true;
  try {
    if (profileDirty.value) {
      await saveProviderProfile();
    }

    const result = await action();
    showToast(`${label}：${render(result)}`, "ok", 6200);
  } catch (error) {
    showToast(error instanceof Error ? error.message : `${label}失败`, "error");
  } finally {
    utilityBusy.value = false;
  }
}

function formatBalanceToast(result: ProviderBalanceResult): string {
  if (!result.Balances.length) {
    return result.Message;
  }

  return `${result.Message} ${result.Balances.map(formatBalance).join("；")}`;
}

function formatBalance(balance: ProviderBalanceInfo): string {
  const extra = [balance.GrantedBalance, balance.ToppedUpBalance].filter(Boolean).join("/");
  return `${balance.Currency} ${balance.TotalBalance}${extra ? ` (${extra})` : ""}`;
}

async function testProfile(): Promise<void> {
  await runProfileUtility(
    "连接测试",
    () => api<ProviderTestResult>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/test`, { method: "POST" }),
    (result) => result.Message);
}

async function fetchProfileModels(): Promise<void> {
  await runProfileUtility(
    "模型列表",
    () => api<ProviderModelsResult>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/models`),
    (result) => result.Models.length ? `${result.Message}：${result.Models.slice(0, 6).map((model) => model.Id).join("、")}` : result.Message);
}

async function fetchProfileBalance(): Promise<void> {
  await runProfileUtility(
    "余额",
    () => api<ProviderBalanceResult>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/balance`),
    formatBalanceToast);
}

function restoreDefaultPrompt(): void {
  form.PromptTemplates[activePromptTemplateKey.value] = defaultPromptTemplates.value[activePromptTemplateKey.value] ?? "";
  markDirty();
}

function restoreAllPromptTemplates(): void {
  applyPromptTemplates(createPromptTemplates(), defaultPromptTemplates.value);
  markDirty();
}

async function pickLlamaCppModel(): Promise<void> {
  if (!isProfileLlamaCpp.value) {
    showToast("请先选择本地模型配置。", "warn");
    return;
  }

  llamaCppModelPicking.value = true;
  try {
    const result = await api<LlamaCppModelPickResult>("/api/llamacpp/model/pick", { method: "POST" });
    if (result.Status === "selected" && result.FilePath) {
      profileForm.LlamaCppModelPath = result.FilePath;
      markProfileDirty();
      showToast(result.Message, "ok");
    } else if (result.Status !== "cancelled") {
      showToast(result.Message, result.Status === "error" ? "error" : "warn");
    }
  } finally {
    llamaCppModelPicking.value = false;
  }
}

async function loadLlamaCppModelPresets(): Promise<void> {
  try {
    const presets = await api<LlamaCppModelDownloadPreset[]>("/api/llamacpp/model/presets");
    llamaCppModelPresets.value = presets;
    if (!llamaCppSelectedPresetId.value && presets.length) {
      llamaCppSelectedPresetId.value = presets[0].Id;
    }
  } catch (error) {
    showToast(error instanceof Error ? error.message : "读取模型列表失败。", "warn");
  }
}

function openLlamaCppDownloadDialog(): void {
  llamaCppDownloadDialogOpen.value = true;
  if (llamaCppModelPresets.value.length === 0) {
    void loadLlamaCppModelPresets();
  }
}

function closeLlamaCppDownloadDialog(): void {
  llamaCppDownloadDialogOpen.value = false;
}

function applyLlamaCppDownloadStatus(status: LlamaCppModelDownloadStatus): void {
  llamaCppDownloadStatus.value = status;
  if (status.State === "completed" && status.LocalPath && status.LocalPath !== llamaCppCompletedPath.value) {
    profileForm.LlamaCppModelPath = status.LocalPath;
    llamaCppCompletedPath.value = status.LocalPath;
    markProfileDirty();
    showToast("已下载并填入模型路径，请保存本地模型配置。", "ok", 5600);
    return;
  }

  if (status.State === "error") {
    showToast(status.Message || "模型下载失败。", "error", 6200);
  }
}

async function pollLlamaCppDownloadStatus(): Promise<void> {
  try {
    const status = await api<LlamaCppModelDownloadStatus>("/api/llamacpp/model/download");
    applyLlamaCppDownloadStatus(status);
    if (status.State === "downloading") {
      window.setTimeout(() => void pollLlamaCppDownloadStatus(), 1000);
    }
  } catch (error) {
    showToast(error instanceof Error ? error.message : "刷新模型下载进度失败。", "warn");
  }
}

async function downloadLlamaCppPreset(): Promise<void> {
  const preset = selectedLlamaCppPreset.value;
  if (!preset) {
    showToast("请先选择一个模型。", "warn");
    return;
  }

  try {
    llamaCppCompletedPath.value = null;
    const request: LlamaCppModelDownloadRequest = { PresetId: preset.Id };
    const status = await api<LlamaCppModelDownloadStatus>("/api/llamacpp/model/download", {
      method: "POST",
      body: request
    });
    applyLlamaCppDownloadStatus(status);
    if (status.State === "downloading") {
      llamaCppDownloadDialogOpen.value = false;
      showToast("已开始下载模型。", "info", 2800);
      window.setTimeout(() => void pollLlamaCppDownloadStatus(), 1000);
    }
  } catch (error) {
    showToast(error instanceof Error ? error.message : "启动模型下载失败。", "error");
  }
}

async function cancelLlamaCppDownload(): Promise<void> {
  try {
    const status = await api<LlamaCppModelDownloadStatus>("/api/llamacpp/model/download/cancel", { method: "POST" });
    applyLlamaCppDownloadStatus(status);
    showToast(status.Message || "已取消模型下载。", "info");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "取消模型下载失败。", "error");
  }
}

async function startLlamaCpp(): Promise<void> {
  if (!isProfileLlamaCpp.value || !profileForm.Id) {
    showToast("请先保存本地模型配置。", "warn");
    return;
  }

  llamaCppBusy.value = true;
  try {
    if (profileDirty.value) {
      await saveProviderProfile();
    }

    const status = await api<LlamaCppServerStatus>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/start`, { method: "POST" });
    if (status.State !== "error") {
      profileForm.LlamaCppAutoStartOnStartup = true;
    }
    await refreshState({ quiet: true });
    showToast(status.Message, status.State === "error" ? "error" : "ok", 5200);
  } finally {
    llamaCppBusy.value = false;
  }
}

async function stopLlamaCpp(): Promise<void> {
  if (!isProfileLlamaCpp.value || !profileForm.Id) {
    showToast("请先选择本地模型配置。", "warn");
    return;
  }

  llamaCppBusy.value = true;
  try {
    const status = await api<LlamaCppServerStatus>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/stop`, { method: "POST" });
    profileForm.LlamaCppAutoStartOnStartup = false;
    await refreshState({ quiet: true });
    showToast(status.Message, "ok");
  } finally {
    llamaCppBusy.value = false;
  }
}

async function runLlamaCppBenchmark(): Promise<void> {
  if (!isProfileLlamaCpp.value || !profileForm.Id) {
    showToast("请先保存本地模型配置。", "warn");
    return;
  }

  llamaCppBenchmarkBusy.value = true;
  try {
    if (profileDirty.value) {
      await saveProviderProfile();
    }

    llamaCppBenchmarkResult.value = await api<LlamaCppBenchmarkResult>(`/api/provider-profiles/${encodeURIComponent(profileForm.Id)}/benchmark`, { method: "POST" });
    await refreshState({ quiet: true });
    showToast(llamaCppBenchmarkResult.value.Message, llamaCppBenchmarkResult.value.Succeeded ? "ok" : "error", 6200);
  } finally {
    llamaCppBenchmarkBusy.value = false;
  }
}

function formatLlamaConfig(config: LlamaCppConfig | null): string {
  if (!config) {
    return "无";
  }

  return `slots ${config.ParallelSlots} / batch ${config.BatchSize} / ubatch ${config.UBatchSize} / FA ${config.FlashAttentionMode}`;
}

function formatBenchmarkCandidate(candidate: LlamaCppBenchmarkCandidate): string {
  const speed = candidate.TotalTokensPerSecond == null ? "失败" : `${candidate.TotalTokensPerSecond.toFixed(1)} tok/s`;
  return `${candidate.Tool} slots ${candidate.ParallelSlots} batch ${candidate.BatchSize}/${candidate.UBatchSize} ${candidate.FlashAttentionMode}: ${speed}`;
}

watch(llamaCppIsActive, (active) => {
  if (active && llamaCppModelPresets.value.length === 0) {
    void loadLlamaCppModelPresets();
    void pollLlamaCppDownloadStatus();
  }
}, { immediate: true });

watch(() => controlPanelStore.state, (state) => {
  applyState(state);
  selectFirstProfileIfNeeded();
}, { immediate: true });

watch(selectedProfileId, () => applySelectedProfile(true));
</script>

<template>
  <section class="page active" id="page-ai">
    <div class="page-head">
      <div>
        <h1>AI 翻译</h1>
        <p>在线服务商按配置优先级执行，llama.cpp 保持独立本地模型流程。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="controlPanelStore.isRefreshing" @click="refreshState()">
          <RotateCcw class="button-icon" />
          刷新
        </button>
        <button class="primary" type="button" :disabled="!formDirty" @click="saveGlobalConfig()">
          <Save class="button-icon" />
          保存 AI 行为
        </button>
      </div>
    </div>

    <div class="settings-stack ai-settings-stack">
      <SectionPanel title="全局 AI 行为" :icon="Bot">
        <div class="form-grid four">
          <label class="field help-target" data-help="翻译输出使用的目标语言。">
            <span class="field-label"><Bot class="field-label-icon" />目标语言</span>
            <select id="targetLanguage" v-model="form.TargetLanguage" @change="markDirty">
              <option v-for="option in targetLanguageOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
          </label>
          <label class="field help-target" data-help="留空时使用 Unity 当前游戏名。">
            <span class="field-label"><Gamepad2 class="field-label-icon" />游戏名称</span>
            <input id="gameTitle" v-model="form.GameTitle" autocomplete="off" :placeholder="automaticGameTitle || '自动检测'" @input="markDirty">
          </label>
          <label class="field help-target" :data-help="activeStyleHint">
            <span class="field-label"><MessageSquareText class="field-label-icon" />翻译风格</span>
            <select id="translationStyle" v-model.number="form.Style" @change="markDirty">
              <option :value="0">忠实</option>
              <option :value="1">自然</option>
              <option :value="2">本地化</option>
              <option :value="3">UI 短句</option>
            </select>
          </label>
          <label class="field help-target" data-help="单批次翻译最多包含多少源文本字符。">
            <span class="field-label"><Gauge class="field-label-icon" />批次字符上限</span>
            <input id="maxBatchCharacters" v-model.number="form.MaxBatchCharacters" type="number" min="256" max="8000" @input="markDirty">
          </label>
        </div>
        <div class="checks">
          <label class="check help-target" data-help="把同组件或同场景附近文本作为参考发给 AI。"><input id="enableTranslationContext" v-model="form.EnableTranslationContext" type="checkbox" @change="markDirty">启用翻译上下文</label>
          <label class="check help-target" data-help="启用术语库约束。"><input id="enableGlossary" v-model="form.EnableGlossary" type="checkbox" @change="markDirty">启用术语库</label>
          <label class="check help-target" data-help="允许 AI 从已捕获文本中抽取候选术语，默认建议关闭。"><input id="enableAutoTermExtraction" v-model="form.EnableAutoTermExtraction" type="checkbox" @change="markDirty">自动抽取术语</label>
        </div>
        <div class="form-grid four">
          <label v-if="form.EnableTranslationContext" class="field help-target" data-help="每条请求最多带入多少条上下文示例。">
            <span class="field-label"><ListChecks class="field-label-icon" />上下文示例数</span>
            <input id="translationContextMaxExamples" v-model.number="form.TranslationContextMaxExamples" type="number" min="0" @input="markDirty">
          </label>
          <label v-if="form.EnableTranslationContext" class="field help-target" data-help="限制上下文示例占用字符数。">
            <span class="field-label"><MessageSquareText class="field-label-icon" />上下文字符数</span>
            <input id="translationContextMaxCharacters" v-model.number="form.TranslationContextMaxCharacters" type="number" min="0" @input="markDirty">
          </label>
          <label class="field help-target" data-help="每次请求最多使用多少条术语。">
            <span class="field-label"><ListChecks class="field-label-icon" />术语数量</span>
            <input id="glossaryMaxTerms" v-model.number="form.GlossaryMaxTerms" type="number" min="0" @input="markDirty">
          </label>
          <label class="field help-target" data-help="术语提示最多占用多少字符。">
            <span class="field-label"><MessageSquareText class="field-label-icon" />术语字符数</span>
            <input id="glossaryMaxCharacters" v-model.number="form.GlossaryMaxCharacters" type="number" min="0" @input="markDirty">
          </label>
        </div>
      </SectionPanel>

      <SectionPanel title="服务商配置" :icon="KeyRound">
        <div class="provider-profile-toolbar">
          <div>
            <span>当前配置</span>
            <strong>{{ activeProviderProfileName }}</strong>
          </div>
          <div class="actions inline-actions">
            <button class="secondary" type="button" @click="saveUseOnlineProfiles"><Bot class="button-icon" />使用配置队列</button>
            <button class="secondary" type="button" @click="openNewProviderProfile(0)"><Plus class="button-icon" />新建在线配置</button>
            <button class="secondary" type="button" :disabled="hasLlamaCppProfile" @click="createLlamaCppProfile"><Server class="button-icon" />新建本地模型</button>
            <button class="secondary" type="button" @click="openImportPicker"><FileInput class="button-icon" />导入</button>
            <input ref="importInput" class="sr-only" type="file" accept=".hutprovider" @change="importProviderProfile">
          </div>
        </div>

        <div class="provider-profile-manager">
          <div class="provider-profile-list" aria-label="服务商配置列表">
            <div
              v-for="profile in providerProfiles"
              :key="profile.Id"
              class="provider-profile-card"
              :class="{ active: selectedProfileId === profile.Id, current: profile.IsActive, cooling: profile.CooldownRemainingSeconds > 0 }">
              <button class="provider-profile-select" type="button" @click="selectedProfileId = profile.Id">
                <span class="profile-rank">#{{ profile.Priority + 1 }}</span>
                <span class="profile-main">
                  <strong>{{ profile.Name }}</strong>
                  <small>{{ formatProviderKind(profile.Kind) }} / {{ profile.Model }}</small>
                </span>
                <span class="profile-status">{{ formatProfileStatus(profile) }}</span>
              </button>
              <div class="provider-card-actions">
                <button class="secondary icon-button" type="button" title="编辑" @click.stop="openProviderProfileEditor(profile)"><Settings2 /></button>
                <button class="secondary icon-button" type="button" :disabled="profile.Priority <= 0" title="上移" @click.stop="moveProviderProfile(profile, -1)"><ArrowUp /></button>
                <button class="secondary icon-button" type="button" :disabled="profile.Priority >= providerProfiles.length - 1" title="下移" @click.stop="moveProviderProfile(profile, 1)"><ArrowDown /></button>
                <button class="secondary icon-button" type="button" title="导出" @click.stop="exportProviderProfile(profile)"><FileOutput /></button>
                <button class="danger icon-button" type="button" title="删除" @click.stop="deleteProviderProfile(profile)"><Trash2 /></button>
              </div>
            </div>
            <div v-if="!providerProfiles.length" class="empty-state">还没有服务商配置</div>
          </div>
        </div>
      </SectionPanel>

      <SectionPanel title="提示词" :icon="MessageSquareText">
        <div class="prompt-editor-head">
          <span class="prompt-mode">{{ promptModeText }}</span>
          <div class="actions inline-actions prompt-editor-actions">
            <button id="restorePromptTemplate" class="secondary" type="button" :disabled="promptTemplateUsesDefault(activePromptTemplateKey)" @click="restoreDefaultPrompt"><RotateCcw class="button-icon" />恢复当前模板</button>
            <button id="restoreAllPromptTemplates" class="secondary" type="button" :disabled="promptUsesDefault" @click="restoreAllPromptTemplates"><RotateCcw class="button-icon" />恢复全部内置</button>
          </div>
        </div>
        <div class="prompt-template-tabs" role="tablist" aria-label="提示词模板">
          <button
            v-for="field in promptTemplateFields"
            :key="field.key"
            class="prompt-template-tab help-target"
            :class="{ active: activePromptTemplateKey === field.key }"
            :data-help="field.help"
            type="button"
            @click="activePromptTemplateKey = field.key">
            {{ field.label }}
          </button>
        </div>
        <div class="prompt-template-meta">
          <strong>{{ activePromptTemplate.label }}</strong>
          <span>占位符</span>
          <code v-for="placeholder in activePromptTemplate.placeholders" :key="placeholder">{{ placeholder }}</code>
          <span v-if="activePromptTemplate.placeholders.length === 0">无必需占位符</span>
        </div>
        <textarea id="customPrompt" class="prompt-editor-field" v-model="activePromptTemplateText" rows="12" spellcheck="false"></textarea>
      </SectionPanel>
    </div>

    <div v-if="providerEditorOpen" class="provider-editor-backdrop" @click.self="closeProviderProfileEditor">
      <section class="provider-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="providerEditorTitle">
        <div class="provider-profile-editor">
          <div class="provider-editor-head">
            <div>
              <span>配置编辑器</span>
              <strong id="providerEditorTitle">{{ profileForm.Id ? profileForm.Name || "未命名配置" : "新建配置" }}</strong>
            </div>
            <button class="secondary" type="button" @click="closeProviderProfileEditor"><X class="button-icon" />关闭</button>
          </div>

          <div class="checks">
            <label class="check"><input id="providerProfileEnabled" v-model="profileForm.Enabled" type="checkbox" @change="markProfileDirty">启用此配置</label>
            <label v-if="!isProfileLlamaCpp" class="check"><input id="providerProfileClearKey" v-model="profileForm.ClearApiKey" type="checkbox" @change="markProfileDirty">清空已保存 Key</label>
          </div>

          <div class="form-grid two">
            <label class="field help-target" data-help="控制面板中显示的配置名称。">
              <span class="field-label"><KeyRound class="field-label-icon" />名称</span>
              <input id="providerProfileName" v-model="profileForm.Name" autocomplete="off" @input="markProfileDirty">
            </label>
            <label class="field help-target" data-help="在线配置和本地模型配置会按优先级依次尝试；本地模型最多只能创建一个。">
              <span class="field-label"><Bot class="field-label-icon" />服务商</span>
              <select id="providerProfileKind" v-model.number="profileForm.Kind" @change="applyProfileKindDefaults">
                <option v-for="option in providerKindOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
              </select>
            </label>
            <label v-if="!isProfileLlamaCpp" class="field help-target" data-help="服务根地址会写入加密配置文件。">
              <span class="field-label"><Server class="field-label-icon" />Base URL</span>
              <input id="providerProfileBaseUrl" v-model="profileForm.BaseUrl" autocomplete="off" @input="markProfileDirty">
            </label>
            <label v-if="!isProfileLlamaCpp" class="field help-target" data-help="请求接口路径。">
              <span class="field-label"><Settings2 class="field-label-icon" />Endpoint</span>
              <input id="providerProfileEndpoint" v-model="profileForm.Endpoint" autocomplete="off" @input="markProfileDirty">
            </label>
            <label v-if="!isProfileLlamaCpp" class="field help-target" data-help="模型名称。">
              <span class="field-label"><Brain class="field-label-icon" />模型</span>
              <input id="providerProfileModel" v-model="profileForm.Model" autocomplete="off" @input="markProfileDirty">
            </label>
            <label v-if="!isProfileLlamaCpp" class="field help-target" data-help="留空表示不修改已保存 Key；新 Key 会进入加密配置文件。">
              <span class="field-label"><KeyRound class="field-label-icon" />API Key</span>
              <input id="providerProfileApiKey" v-model="profileForm.ApiKey" autocomplete="off" type="password" placeholder="留空不修改" @input="markProfileDirty">
            </label>
          </div>

          <div class="form-grid four">
            <label v-if="!isProfileLlamaCpp" class="field help-target" data-help="此配置可同时执行的在线请求数。">
              <span class="field-label"><Gauge class="field-label-icon" />并发</span>
              <input id="providerProfileMaxConcurrentRequests" v-model.number="profileForm.MaxConcurrentRequests" type="number" min="1" max="100" @input="markProfileDirty">
            </label>
            <label v-if="!isProfileLlamaCpp" class="field help-target" data-help="此配置每分钟最多发送的请求数。">
              <span class="field-label"><Clock3 class="field-label-icon" />RPM</span>
              <input id="providerProfileRequestsPerMinute" v-model.number="profileForm.RequestsPerMinute" type="number" min="1" max="600" @input="markProfileDirty">
            </label>
            <label class="field help-target" data-help="此配置单次请求超时时间。">
              <span class="field-label"><Clock3 class="field-label-icon" />超时(秒)</span>
              <input id="providerProfileRequestTimeoutSeconds" v-model.number="profileForm.RequestTimeoutSeconds" type="number" min="5" max="180" @input="markProfileDirty">
            </label>
            <label v-if="isProfileDeepSeek || isProfileOpenAiCompatible" class="field help-target" data-help="留空表示使用服务默认值。">
              <span class="field-label"><Thermometer class="field-label-icon" />Temperature</span>
              <input id="providerProfileTemperature" v-model="profileForm.Temperature" type="number" min="0" max="2" step="0.1" @input="markProfileDirty">
            </label>
            <label v-if="isProfileOpenAi" class="field help-target" data-help="普通翻译建议 none。">
              <span class="field-label"><Brain class="field-label-icon" />OpenAI 推理</span>
              <select id="providerProfileReasoningEffort" v-model="profileForm.ReasoningEffort" @change="markProfileDirty">
                <option value="none">none</option>
                <option value="low">low</option>
                <option value="medium">medium</option>
                <option value="high">high</option>
                <option value="xhigh">xhigh</option>
                <option value="max">max</option>
              </select>
            </label>
            <label v-if="isProfileOpenAi" class="field help-target" data-help="普通翻译建议 low。">
              <span class="field-label"><Settings2 class="field-label-icon" />输出详细度</span>
              <select id="providerProfileOutputVerbosity" v-model="profileForm.OutputVerbosity" @change="markProfileDirty">
                <option value="low">low</option>
                <option value="medium">medium</option>
                <option value="high">high</option>
              </select>
            </label>
            <label v-if="isProfileDeepSeek" class="field help-target" data-help="普通 UI 翻译建议 disabled。">
              <span class="field-label"><Brain class="field-label-icon" />DeepSeek Thinking</span>
              <select id="providerProfileDeepSeekThinkingMode" v-model="profileForm.DeepSeekThinkingMode" @change="markProfileDirty">
                <option value="disabled">disabled</option>
                <option value="enabled">enabled</option>
              </select>
            </label>
          </div>

          <div v-if="isProfileOpenAiCompatible" class="ai-compatible-advanced">
            <label class="field textarea-field help-target" data-help="每行一个 Header-Name: value；Authorization 和 Content-Type 由插件维护。">
              <span class="field-label"><ListChecks class="field-label-icon" />自定义请求头</span>
              <textarea id="providerProfileCustomHeaders" v-model="profileForm.OpenAICompatibleCustomHeaders" rows="4" spellcheck="false" @input="markProfileDirty"></textarea>
            </label>
            <label class="field textarea-field help-target" data-help="附加到兼容请求体的 JSON object。">
              <span class="field-label"><Settings2 class="field-label-icon" />额外请求体 JSON</span>
              <textarea id="providerProfileExtraBodyJson" v-model="profileForm.OpenAICompatibleExtraBodyJson" rows="4" spellcheck="false" @input="markProfileDirty"></textarea>
            </label>
          </div>

          <div v-if="isProfileLlamaCpp" class="llama-local-panel">
            <div class="field llama-model-row help-target" data-help="选择本地 GGUF 模型文件；此配置进入服务商优先级队列后，轮到它时会自动启动 llama.cpp。">
              <span class="field-label"><FolderOpen class="field-label-icon" />GGUF 模型文件</span>
              <div class="input-action-row model-path-actions">
                <input id="llamaCppModelPath" v-model="profileForm.LlamaCppModelPath" autocomplete="off" placeholder="选择 .gguf 模型文件" @input="markProfileDirty">
                <button id="pickLlamaCppModel" class="secondary" type="button" :disabled="llamaCppModelPicking" @click="pickLlamaCppModel">
                  <FolderOpen class="button-icon" />
                  {{ llamaCppModelPicking ? "选择中..." : "选择模型" }}
                </button>
                <button id="openLlamaCppModelDownload" class="secondary" type="button" @click="openLlamaCppDownloadDialog">
                  <Download class="button-icon" />
                  模型下载
                </button>
              </div>
            </div>
            <div v-if="isLlamaCppDownloading" class="llama-download-progress">
              <div class="llama-download-bar"><span :style="{ width: `${llamaCppDownloadProgressPercent}%` }"></span></div>
              <strong>{{ llamaCppDownloadText }}</strong>
            </div>
            <div class="llama-status-strip">
              <div><span>安装</span><strong>{{ llamaCppInstallText }}</strong></div>
              <div><span>状态</span><strong>{{ llamaCppStateText }}</strong></div>
              <div class="llama-result-card"><span>结果</span><strong>{{ llamaCppStatusText }}</strong></div>
            </div>
            <div class="llama-run-row">
              <label class="field"><span class="field-label"><MessageSquareText class="field-label-icon" />上下文长度</span><input id="llamaCppContextSize" v-model.number="profileForm.LlamaCppContextSize" type="number" min="512" @input="markProfileDirty"></label>
              <label class="field"><span class="field-label"><Layers class="field-label-icon" />GPU 层数</span><input id="llamaCppGpuLayers" v-model.number="profileForm.LlamaCppGpuLayers" type="number" min="0" max="999" @input="markProfileDirty"></label>
              <label class="field"><span class="field-label"><Gauge class="field-label-icon" />并行槽位</span><input id="llamaCppParallelSlots" v-model.number="profileForm.LlamaCppParallelSlots" type="number" min="1" max="16" @input="markProfileDirty"></label>
              <div class="actions inline-actions llama-run-actions">
                <button v-if="!llamaCppIsActive" id="startLlamaCpp" class="primary" type="button" :disabled="llamaCppBusy || !profileForm.Id" @click="startLlamaCpp"><Play class="button-icon" />{{ llamaCppBusy ? "处理中..." : "启动本地模型" }}</button>
                <button v-else id="stopLlamaCpp" class="secondary" type="button" :disabled="llamaCppBusy || !profileForm.Id" @click="stopLlamaCpp"><Square class="button-icon" />{{ llamaCppBusy ? "处理中..." : "停止本地模型" }}</button>
              </div>
            </div>
            <div class="llama-tune-row">
              <label class="field"><span class="field-label"><Gauge class="field-label-icon" />Batch</span><input id="llamaCppBatchSize" v-model.number="profileForm.LlamaCppBatchSize" type="number" min="128" max="8192" @input="markProfileDirty"></label>
              <label class="field"><span class="field-label"><Gauge class="field-label-icon" />UBatch</span><input id="llamaCppUBatchSize" v-model.number="profileForm.LlamaCppUBatchSize" type="number" min="64" max="4096" @input="markProfileDirty"></label>
              <label class="field"><span class="field-label"><Zap class="field-label-icon" />Flash Attention</span>
                <select id="llamaCppFlashAttentionMode" v-model="profileForm.LlamaCppFlashAttentionMode" @change="markProfileDirty">
                  <option value="auto">auto</option>
                  <option value="on">on</option>
                  <option value="off">off</option>
                </select>
              </label>
              <div class="actions inline-actions llama-run-actions">
                <button id="runLlamaCppBenchmark" class="secondary" type="button" :disabled="llamaCppBenchmarkBusy || llamaCppIsActive || !profileForm.Id" @click="runLlamaCppBenchmark"><Gauge class="button-icon" />{{ llamaCppBenchmarkBusy ? "基准运行中..." : "运行 CUDA 基准" }}</button>
              </div>
            </div>
            <div v-if="llamaCppBenchmarkResult" class="llama-benchmark-result">
              <div><span>当前配置</span><strong>{{ formatLlamaConfig(llamaCppBenchmarkResult.CurrentConfig) }}</strong></div>
              <div><span>推荐配置</span><strong>{{ formatLlamaConfig(llamaCppBenchmarkResult.RecommendedConfig) }}</strong></div>
              <div><span>自动保存</span><strong>{{ llamaCppBenchmarkResult.Saved ? "已保存" : "未保存" }}</strong></div>
              <div class="llama-benchmark-list">
                <span>吞吐结果</span>
                <strong v-for="candidate in llamaCppBenchmarkResult.Candidates" :key="`${candidate.Tool}-${candidate.ParallelSlots}-${candidate.BatchSize}-${candidate.UBatchSize}-${candidate.FlashAttentionMode}`">{{ formatBenchmarkCandidate(candidate) }}</strong>
              </div>
            </div>
          </div>

          <div class="actions provider-profile-actions">
            <button class="primary" type="button" :disabled="profileBusy" @click="saveProviderProfile({ closeEditor: true })"><Save class="button-icon" />{{ profileForm.Id ? "保存配置" : "添加配置" }}</button>
            <button class="secondary" type="button" :disabled="utilityBusy || !profileForm.Id" @click="testProfile"><Zap class="button-icon" />测试连接</button>
            <button v-if="!isProfileLlamaCpp" class="secondary" type="button" :disabled="utilityBusy || !profileForm.Id" @click="fetchProfileModels"><Download class="button-icon" />获取模型</button>
            <button v-if="!isProfileLlamaCpp" class="secondary" type="button" :disabled="utilityBusy || !profileForm.Id" @click="fetchProfileBalance"><WalletCards class="button-icon" />查询余额</button>
          </div>
        </div>
      </section>
    </div>

    <div v-if="llamaCppDownloadDialogOpen" class="model-download-backdrop" @click.self="closeLlamaCppDownloadDialog">
      <section class="model-download-dialog" role="dialog" aria-modal="true" aria-labelledby="modelDownloadTitle">
        <div class="model-download-head">
          <div>
            <h2 id="modelDownloadTitle">模型下载</h2>
            <p>选择 GGUF 模型，下载完成后会自动填入本地模型路径。</p>
          </div>
          <button class="secondary" type="button" @click="closeLlamaCppDownloadDialog"><X class="button-icon" />关闭</button>
        </div>
        <div v-if="llamaCppModelPresets.length" class="field help-target" data-help="下载完成后会填入本地模型路径，保存本地模型配置后生效。">
          <span class="field-label"><Download class="field-label-icon" />模型</span>
          <div id="llamaCppPresetList" class="model-preset-list" role="radiogroup" aria-label="模型下载列表">
            <button
              v-for="preset in llamaCppModelPresets"
              :key="preset.Id"
              class="model-preset-card"
              :class="{ 'model-preset-card-active': preset.Id === llamaCppSelectedPresetId }"
              type="button"
              role="radio"
              :aria-checked="preset.Id === llamaCppSelectedPresetId"
              :disabled="isLlamaCppDownloading"
              @click="llamaCppSelectedPresetId = preset.Id">
              <span class="model-preset-title">{{ preset.Label }}</span>
              <span class="model-preset-meta">
                <strong>{{ preset.Quantization }}</strong>
                <span>{{ formatBytes(preset.FileSizeBytes) }}</span>
                <span v-if="preset.License !== 'apache-2.0'" class="model-preset-license">{{ preset.License }}</span>
              </span>
              <span class="model-preset-use">{{ preset.UseCase }}</span>
              <span class="model-preset-notes">{{ preset.Notes }}</span>
            </button>
          </div>
        </div>
        <p v-else class="hint">正在读取模型列表...</p>
        <div class="actions model-download-actions">
          <button id="downloadLlamaCppPreset" class="primary help-target" data-help="由插件进程下载 GGUF 文件；不会覆盖校验不匹配的同名文件。" type="button" :disabled="isLlamaCppDownloading || !selectedLlamaCppPreset" @click="downloadLlamaCppPreset"><Download class="button-icon" />{{ isLlamaCppDownloading ? "下载中..." : "下载模型" }}</button>
          <button id="cancelLlamaCppDownload" class="secondary help-target" data-help="取消当前模型下载，并删除未完成的 .part 临时文件。" type="button" :disabled="!isLlamaCppDownloading" @click="cancelLlamaCppDownload"><X class="button-icon" />取消下载</button>
        </div>
      </section>
    </div>
  </section>
</template>
