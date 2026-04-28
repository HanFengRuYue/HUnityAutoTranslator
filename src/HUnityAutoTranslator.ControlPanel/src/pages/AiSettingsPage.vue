<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import {
  Bot,
  Brain,
  Clock3,
  FileKey,
  FolderOpen,
  Gauge,
  Gamepad2,
  Globe2,
  KeyRound,
  Layers,
  ListChecks,
  ListRestart,
  MessageSquareText,
  Play,
  RotateCcw,
  Save,
  Server,
  Settings2,
  Square,
  Thermometer,
  WalletCards,
  Zap
} from "lucide-vue-next";
import { api } from "../api/client";
import SectionPanel from "../components/SectionPanel.vue";
import {
  controlPanelStore,
  refreshState,
  saveApiKey,
  saveConfig,
  setDirtyForm,
  showToast
} from "../state/controlPanelStore";
import type {
  ControlPanelState,
  LlamaCppBenchmarkCandidate,
  LlamaCppBenchmarkResult,
  LlamaCppConfig,
  LlamaCppModelPickResult,
  LlamaCppServerStatus,
  PromptTemplateConfig,
  ProviderBalanceInfo,
  ProviderBalanceResult,
  ProviderModelsResult,
  ProviderTestResult,
  UpdateConfigRequest
} from "../types/api";

const formKey = "ai";
type PromptTemplateKey = keyof PromptTemplateConfig;

interface SaveBehavior {
  quiet?: boolean;
}

const promptTemplateFields: Array<{
  key: PromptTemplateKey;
  label: string;
  placeholders: string[];
  required: string[];
}> = [
  {
    key: "SystemPrompt",
    label: "系统提示词",
    placeholders: ["{TargetLanguage}", "{StyleInstruction}", "{GameTitle}", "{GameContext}", "{GlossarySystemPolicy}"],
    required: []
  },
  {
    key: "GlossarySystemPolicy",
    label: "术语库系统约束",
    placeholders: [],
    required: []
  },
  {
    key: "BatchUserPrompt",
    label: "批量翻译请求",
    placeholders: ["{PromptSections}", "{InputJson}"],
    required: ["{InputJson}"]
  },
  {
    key: "GlossaryTermsSection",
    label: "术语库条目片段",
    placeholders: ["{GlossaryTermsJson}"],
    required: ["{GlossaryTermsJson}"]
  },
  {
    key: "CurrentItemContextSection",
    label: "当前文本上下文片段",
    placeholders: ["{ItemContextsJson}"],
    required: ["{ItemContextsJson}"]
  },
  {
    key: "ItemHintsSection",
    label: "短文本提示片段",
    placeholders: ["{ItemHintsJson}"],
    required: ["{ItemHintsJson}"]
  },
  {
    key: "ContextExamplesSection",
    label: "历史上下文片段",
    placeholders: ["{ContextExamplesJson}"],
    required: ["{ContextExamplesJson}"]
  },
  {
    key: "GlossaryRepairPrompt",
    label: "术语修复提示词",
    placeholders: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RequiredGlossaryTermsJson}", "{RequiredGlossaryTermsBlock}"],
    required: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}"]
  },
  {
    key: "QualityRepairPrompt",
    label: "质量修复提示词",
    placeholders: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RepairContextJson}", "{GameTitle}"],
    required: ["{SourceText}", "{InvalidTranslation}", "{FailureReason}", "{RepairContextJson}"]
  },
  {
    key: "GlossaryExtractionSystemPrompt",
    label: "自动术语抽取系统",
    placeholders: [],
    required: []
  },
  {
    key: "GlossaryExtractionUserPrompt",
    label: "自动术语抽取请求",
    placeholders: ["{RowsJson}"],
    required: ["{RowsJson}"]
  }
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

const providerNames: Record<number, string> = {
  0: "OpenAI",
  1: "DeepSeek",
  3: "llama.cpp 本地模型",
  2: "OpenAI 兼容"
};

const providerDefaults: Record<number, { baseUrl: string; endpoint: string; model: string }> = {
  0: { baseUrl: "https://api.openai.com", endpoint: "/v1/responses", model: "gpt-5.5" },
  1: { baseUrl: "https://api.deepseek.com", endpoint: "/chat/completions", model: "deepseek-v4-flash" },
  3: { baseUrl: "http://127.0.0.1:0", endpoint: "/v1/chat/completions", model: "local-model" },
  2: { baseUrl: "http://127.0.0.1:8000", endpoint: "/v1/chat/completions", model: "local-model" }
};

const modelPresets: Record<number, Array<{ value: string; label: string }>> = {
  0: [
    { value: "gpt-5.5", label: "GPT-5.5" },
    { value: "gpt-5.4", label: "GPT-5.4" },
    { value: "gpt-5.4-mini", label: "GPT-5.4 Mini" },
    { value: "custom", label: "手动填写" }
  ],
  1: [
    { value: "deepseek-v4-flash", label: "DeepSeek V4 Flash" },
    { value: "deepseek-v4-pro", label: "DeepSeek V4 Pro" },
    { value: "deepseek-chat", label: "DeepSeek Chat" },
    { value: "deepseek-reasoner", label: "DeepSeek Reasoner" },
    { value: "custom", label: "手动填写" }
  ],
  2: [
    { value: "local-model", label: "本地/兼容模型" },
    { value: "custom", label: "手动填写" }
  ],
  3: [
    { value: "local-model", label: "llama.cpp local-model" },
    { value: "custom", label: "手动填写" }
  ]
};

const styleHints: Record<number, string> = {
  0: "忠实：尽量保留原文含义和语气。",
  1: "自然：优先输出流畅中文。",
  2: "本地化：适合游戏语境和 UI 文案。",
  3: "简短：菜单、按钮和提示更短。"
};

const form = reactive({
  TargetLanguage: "zh-Hans",
  GameTitle: "",
  Style: 2,
  ProviderKind: 0,
  BaseUrl: "",
  Endpoint: "",
  Model: "",
  ModelPreset: "custom",
  ApiKey: "",
  RequestTimeoutSeconds: 30,
  MaxConcurrentRequests: 4,
  RequestsPerMinute: 60,
  MaxBatchCharacters: 1800,
  ReasoningEffort: "none",
  DeepSeekReasoningEffort: "none",
  OutputVerbosity: "low",
  DeepSeekThinkingMode: "disabled",
  Temperature: "",
  CustomPrompt: "",
  PromptTemplates: createPromptTemplates(),
  LlamaCppModelPath: "",
  LlamaCppContextSize: 4096,
  LlamaCppGpuLayers: 999,
  LlamaCppParallelSlots: 1,
  LlamaCppBatchSize: 2048,
  LlamaCppUBatchSize: 512,
  LlamaCppFlashAttentionMode: "auto"
});

const utilityBusy = ref(false);
const llamaCppBusy = ref(false);
const llamaCppModelPicking = ref(false);
const llamaCppBenchmarkBusy = ref(false);
const llamaCppBenchmarkResult = ref<LlamaCppBenchmarkResult | null>(null);
const activePromptTemplateKey = ref<PromptTemplateKey>("SystemPrompt");
const formDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));
const providerOptions = computed(() => modelPresets[form.ProviderKind] ?? modelPresets[0]);
const activeProviderName = computed(() => providerNames[form.ProviderKind] ?? "-");
const activeStyleHint = computed(() => styleHints[form.Style] ?? "");
const isOpenAi = computed(() => form.ProviderKind === 0);
const isDeepSeek = computed(() => form.ProviderKind === 1);
const isLlamaCpp = computed(() => form.ProviderKind === 3);
const canUseTemperature = computed(() => form.ProviderKind === 1 || form.ProviderKind === 2 || form.ProviderKind === 3);
const automaticGameTitle = computed(() => controlPanelStore.state?.AutomaticGameTitle ?? "");
const defaultSystemPrompt = computed(() => controlPanelStore.state?.DefaultSystemPrompt ?? "");
const defaultPromptTemplates = computed(() => controlPanelStore.state?.DefaultPromptTemplates ?? createPromptTemplates());
const promptUsesDefault = computed(() => promptTemplateFields.every((field) => promptTemplateUsesDefault(field.key)));
const promptModeText = computed(() => promptUsesDefault.value ? "正在使用内置提示词" : "正在使用自定义提示词");
const restorePromptText = computed(() => promptUsesDefault.value ? "使用内置提示词" : "恢复内置提示词");
const llamaCppStatus = computed(() => controlPanelStore.state?.LlamaCppStatus);
const llamaCppStatusText = computed(() => llamaCppStatus.value?.Message ?? "本地模型未启动。");
const llamaCppIsActive = computed(() => {
  const state = (llamaCppStatus.value?.State ?? "").toLowerCase();
  return state === "starting" || state === "running";
});
const activePromptTemplate = computed(() =>
  promptTemplateFields.find((field) => field.key === activePromptTemplateKey.value) ?? promptTemplateFields[0]);
const activePromptTemplateText = computed({
  get: () => form.PromptTemplates[activePromptTemplateKey.value] ?? "",
  set: (value: string) => {
    form.PromptTemplates[activePromptTemplateKey.value] = value;
    markDirty();
  }
});
const llamaCppInstallText = computed(() => {
  const status = llamaCppStatus.value;
  if (!status?.Installed) {
    return "未检测到插件内 llama.cpp";
  }

  const release = status.Release ? ` ${status.Release}` : "";
  const variant = status.Variant || status.Backend || "unknown";
  return `${variant}${release}`;
});

function markDirty(): void {
  setDirtyForm(formKey, true);
}

function numberValue(value: number | string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
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

function normalizeDeepSeekEffort(value: unknown): string {
  const normalized = String(value ?? "").trim().toLowerCase();
  if (normalized === "max" || normalized === "xhigh") {
    return "max";
  }

  if (normalized === "high") {
    return "high";
  }

  return "none";
}

function updateModelPresetFromInput(): void {
  const match = providerOptions.value.find((option) => option.value === form.Model);
  form.ModelPreset = match ? match.value : "custom";
}

function applyState(state: ControlPanelState | null, force = false): void {
  if (!state || (!force && formDirty.value)) {
    return;
  }

  form.TargetLanguage = state.TargetLanguage;
  form.GameTitle = state.GameTitle ?? "";
  form.Style = numberValue(state.Style);
  form.ProviderKind = numberValue(state.ProviderKind);
  form.BaseUrl = state.BaseUrl;
  form.Endpoint = state.Endpoint;
  form.Model = state.Model;
  form.RequestTimeoutSeconds = state.RequestTimeoutSeconds;
  form.MaxConcurrentRequests = state.MaxConcurrentRequests;
  form.RequestsPerMinute = state.RequestsPerMinute;
  form.MaxBatchCharacters = state.MaxBatchCharacters;
  form.ReasoningEffort = state.ReasoningEffort || "none";
  form.DeepSeekReasoningEffort = normalizeDeepSeekEffort(state.ReasoningEffort);
  form.OutputVerbosity = state.OutputVerbosity;
  form.DeepSeekThinkingMode = state.DeepSeekThinkingMode || "disabled";
  form.Temperature = state.Temperature === null || state.Temperature === undefined ? "" : String(state.Temperature);
  applyPromptTemplates(state.PromptTemplates, state.DefaultPromptTemplates);
  form.LlamaCppModelPath = state.LlamaCpp?.ModelPath ?? "";
  form.LlamaCppContextSize = state.LlamaCpp?.ContextSize ?? 4096;
  form.LlamaCppGpuLayers = state.LlamaCpp?.GpuLayers ?? 999;
  form.LlamaCppParallelSlots = state.LlamaCpp?.ParallelSlots ?? 1;
  form.LlamaCppBatchSize = state.LlamaCpp?.BatchSize ?? 2048;
  form.LlamaCppUBatchSize = state.LlamaCpp?.UBatchSize ?? 512;
  form.LlamaCppFlashAttentionMode = state.LlamaCpp?.FlashAttentionMode ?? "auto";
  form.ApiKey = "";
  updateModelPresetFromInput();
  setDirtyForm(formKey, false);
}

function activeReasoningEffort(): string {
  return form.ProviderKind === 1 ? form.DeepSeekReasoningEffort : form.ReasoningEffort;
}

function readConfig(): UpdateConfigRequest {
  const temperatureText = form.Temperature.trim();
  const promptTemplateOverrides = buildPromptTemplateOverrides();
  return {
    TargetLanguage: form.TargetLanguage,
    GameTitle: form.GameTitle.trim(),
    Style: numberValue(form.Style),
    ProviderKind: numberValue(form.ProviderKind),
    BaseUrl: form.BaseUrl,
    Endpoint: form.Endpoint,
    Model: isLlamaCpp.value ? "local-model" : form.Model,
    RequestTimeoutSeconds: numberValue(form.RequestTimeoutSeconds),
    MaxConcurrentRequests: numberValue(form.MaxConcurrentRequests),
    RequestsPerMinute: numberValue(form.RequestsPerMinute),
    MaxBatchCharacters: numberValue(form.MaxBatchCharacters),
    ReasoningEffort: activeReasoningEffort(),
    OutputVerbosity: form.OutputVerbosity,
    DeepSeekThinkingMode: form.DeepSeekThinkingMode,
    Temperature: temperatureText === "" ? null : Number(temperatureText),
    ClearTemperature: canUseTemperature.value && temperatureText === "",
    CustomPrompt: promptTemplateOverrides.SystemPrompt ?? "",
    PromptTemplates: promptTemplateOverrides,
    LlamaCpp: {
      ModelPath: form.LlamaCppModelPath.trim() || null,
      ContextSize: numberValue(form.LlamaCppContextSize),
      GpuLayers: numberValue(form.LlamaCppGpuLayers),
      ParallelSlots: numberValue(form.LlamaCppParallelSlots),
      BatchSize: numberValue(form.LlamaCppBatchSize),
      UBatchSize: numberValue(form.LlamaCppUBatchSize),
      FlashAttentionMode: form.LlamaCppFlashAttentionMode
    }
  };
}

async function saveConfigOnly(options: SaveBehavior = {}): Promise<ControlPanelState | null> {
  if (!validatePromptTemplates()) {
    return null;
  }

  const state = await saveConfig(readConfig(), formKey, { quiet: options.quiet });
  applyState(state, true);
  return state;
}

async function savePendingApiKey(options: SaveBehavior = {}): Promise<boolean> {
  const apiKey = form.ApiKey.trim();
  if (!apiKey) {
    return false;
  }

  const state = await saveApiKey(apiKey, { quiet: options.quiet });
  if (state) {
    form.ApiKey = "";
    applyState(state, true);
    return true;
  }

  return false;
}

async function saveAll(): Promise<void> {
  const state = await saveConfigOnly();
  if (!state) {
    return;
  }

  if (isLlamaCpp.value) {
    return;
  }

  const keySaved = await savePendingApiKey();
  if (!keySaved && !form.ApiKey.trim()) {
    showToast("未填写新密钥，已保留当前密钥。", "info");
  }
}

async function saveKeyOnly(): Promise<void> {
  if (!await savePendingApiKey()) {
    showToast("请先填写新的 API Key。", "warn");
  }
}

async function runProviderUtility(label: string, action: () => Promise<void>): Promise<void> {
  utilityBusy.value = true;
  try {
    const state = await saveConfigOnly({ quiet: true });
    if (!state) {
      return;
    }

    await savePendingApiKey({ quiet: true });
    await action();
  } catch (error) {
    showToast(error instanceof Error ? error.message : `${label}失败`, "error");
  } finally {
    utilityBusy.value = false;
  }
}

async function testProvider(): Promise<void> {
  await runProviderUtility("测试连接", async () => {
    const result = await api<ProviderTestResult>("/api/provider/test", { method: "POST", body: {} });
    showToast(result.Message || (result.Succeeded ? "连接可用。" : "连接失败。"), result.Succeeded ? "ok" : "error");
  });
}

function formatModelsToast(result: ProviderModelsResult): string {
  const prefix = result.Succeeded ? `已获取 ${result.Models.length} 个模型。` : (result.Message || "获取模型列表失败。");
  const sample = result.Models.slice(0, 6).map((model) => model.Id).join("、");
  return sample ? `${prefix}\n${sample}` : prefix;
}

async function fetchModels(): Promise<void> {
  await runProviderUtility("获取模型列表", async () => {
    const result = await api<ProviderModelsResult>("/api/provider/models");
    showToast(formatModelsToast(result), result.Succeeded ? "ok" : "warn", 5600);
  });
}

function formatBalanceEntry(balance: ProviderBalanceInfo): string {
  const total = balance.TotalBalance.trim() || "-";
  const parts = [`${balance.Currency.trim()} ${total}`.trim()];
  const details = [
    balance.GrantedBalance ? `赠送 ${balance.GrantedBalance}` : "",
    balance.ToppedUpBalance ? `充值 ${balance.ToppedUpBalance}` : ""
  ].filter(Boolean);

  if (details.length) {
    parts.push(`（${details.join("，")}）`);
  }

  return parts.join("");
}

function formatBalanceToast(result: ProviderBalanceResult): string {
  if (!result.Succeeded) {
    return result.Message || "查询余额/成本失败。";
  }

  if (!result.Balances.length) {
    return "未返回余额/成本记录。";
  }

  const values = result.Balances.map(formatBalanceEntry).join("；");
  if (form.ProviderKind === 1) {
    return `余额：${values}`;
  }

  return `最近 7 天成本：${values}。OpenAI 成本接口通常需要管理员密钥。`;
}

async function fetchBalance(): Promise<void> {
  if (isLlamaCpp.value) {
    showToast("本地模型不适用余额或成本查询。", "info");
    return;
  }

  await runProviderUtility("查询余额/成本", async () => {
    const result = await api<ProviderBalanceResult>("/api/provider/balance");
    showToast(formatBalanceToast(result), result.Succeeded ? "ok" : "warn", 5600);
  });
}

function updateLocalLlamaStatus(status: LlamaCppServerStatus): void {
  if (controlPanelStore.state) {
    controlPanelStore.state.LlamaCppStatus = status;
    controlPanelStore.state.BaseUrl = status.Port > 0 ? `http://127.0.0.1:${status.Port}` : "http://127.0.0.1:0";
  }
}

async function startLlamaCpp(): Promise<void> {
  llamaCppBusy.value = true;
  try {
    const state = await saveConfigOnly({ quiet: true });
    if (!state) {
      return;
    }

    const status = await api<LlamaCppServerStatus>("/api/llamacpp/start", { method: "POST", body: {} });
    updateLocalLlamaStatus(status);
    showToast(status.Message, status.State === "error" ? "error" : "ok", 5200);
  } catch (error) {
    showToast(error instanceof Error ? error.message : "启动本地模型失败。", "error");
  } finally {
    llamaCppBusy.value = false;
  }
}

async function stopLlamaCpp(): Promise<void> {
  llamaCppBusy.value = true;
  try {
    const status = await api<LlamaCppServerStatus>("/api/llamacpp/stop", { method: "POST", body: {} });
    updateLocalLlamaStatus(status);
    showToast(status.Message, "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "停止本地模型失败。", "error");
  } finally {
    llamaCppBusy.value = false;
  }
}

async function pickLlamaCppModel(): Promise<void> {
  llamaCppModelPicking.value = true;
  try {
    const result = await api<LlamaCppModelPickResult>("/api/llamacpp/model/pick", { method: "POST", body: {} });
    if (result.Status === "selected" && result.FilePath) {
      form.LlamaCppModelPath = result.FilePath;
      markDirty();
      showToast("已选择 GGUF 模型文件。", "ok");
      return;
    }

    if (result.Status !== "cancelled") {
      showToast(result.Message || "选择模型文件失败。", result.Status === "unsupported" ? "warn" : "error");
    }
  } catch (error) {
    showToast(error instanceof Error ? error.message : "选择模型文件失败。", "error");
  } finally {
    llamaCppModelPicking.value = false;
  }
}

function formatBenchmarkRate(value: number | null | undefined): string {
  return value === null || value === undefined ? "-" : `${value.toFixed(1)} t/s`;
}

function formatLlamaConfig(config: LlamaCppConfig | null | undefined): string {
  if (!config) {
    return "-";
  }

  return `槽位 ${config.ParallelSlots} / 每槽上下文 ${config.ContextSize} / batch ${config.BatchSize} / ubatch ${config.UBatchSize} / fa ${config.FlashAttentionMode}`;
}

function formatBenchmarkCandidate(candidate: LlamaCppBenchmarkCandidate): string {
  const tool = candidate.Tool === "llama-batched-bench" ? `并行 ${candidate.ParallelSlots}` : "单槽";
  const context = candidate.TotalContextSize > 0 ? ` / 总上下文 ${candidate.TotalContextSize}` : "";
  return `${tool}: prompt ${formatBenchmarkRate(candidate.PromptTokensPerSecond)} / gen ${formatBenchmarkRate(candidate.GenerationTokensPerSecond)} / total ${formatBenchmarkRate(candidate.TotalTokensPerSecond)}${context}`;
}

async function runLlamaCppBenchmark(): Promise<void> {
  if (formDirty.value) {
    showToast("请先保存当前 AI 设置，再运行本地 CUDA 基准。", "warn", 5200);
    return;
  }

  llamaCppBenchmarkBusy.value = true;
  try {
    const result = await api<LlamaCppBenchmarkResult>("/api/llamacpp/benchmark", { method: "POST", body: {} });
    llamaCppBenchmarkResult.value = result;
    showToast(result.Message, result.Succeeded ? "ok" : "warn", 6200);
    if (result.Saved) {
      const state = await refreshState({ quiet: true });
      applyState(state, true);
    }
  } catch (error) {
    showToast(error instanceof Error ? error.message : "本地 CUDA 基准运行失败。", "error");
  } finally {
    llamaCppBenchmarkBusy.value = false;
  }
}

function applyProviderDefaults(): void {
  const defaults = providerDefaults[form.ProviderKind] ?? providerDefaults[0];
  form.BaseUrl = defaults.baseUrl;
  form.Endpoint = defaults.endpoint;
  form.Model = defaults.model;
  form.ReasoningEffort = "none";
  form.DeepSeekReasoningEffort = "none";
  form.DeepSeekThinkingMode = "disabled";
  updateModelPresetFromInput();
  markDirty();
}

function applyModelPreset(): void {
  if (form.ModelPreset !== "custom") {
    form.Model = form.ModelPreset;
    markDirty();
  }
}

function restoreDefaultPrompt(): void {
  restoreActivePromptTemplate();
}

function restoreActivePromptTemplate(): void {
  form.PromptTemplates[activePromptTemplateKey.value] = defaultPromptTemplates.value[activePromptTemplateKey.value] ?? defaultSystemPrompt.value;
  form.CustomPrompt = form.PromptTemplates.SystemPrompt ?? "";
  markDirty();
}

function restoreAllPromptTemplates(): void {
  applyPromptTemplates(null, defaultPromptTemplates.value);
  markDirty();
}

watch(() => controlPanelStore.state, (state) => applyState(state), { immediate: true });
</script>

<template>
  <section class="page active" id="page-ai">
    <div class="page-head">
      <div>
        <h1>AI 翻译设置</h1>
        <p>配置服务商、模型、密钥和 Prompt，检测结果显示在顶部通知中。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="!formDirty" @click="applyState(controlPanelStore.state, true)"><RotateCcw class="button-icon" />还原</button>
        <button class="primary" type="button" @click="saveAll"><Save class="button-icon" />保存 AI 设置</button>
      </div>
    </div>

    <div class="form-stack" @input="markDirty" @change="markDirty">
      <SectionPanel title="服务商" :icon="Bot">
        <div class="ai-provider-grid">
          <label class="field"><span class="field-label"><Server class="field-label-icon" />服务商</span>
            <select id="providerKind" v-model.number="form.ProviderKind" @change="applyProviderDefaults">
              <option :value="0">OpenAI Responses</option>
              <option :value="1">DeepSeek</option>
              <option :value="3">llama.cpp 本地模型</option>
              <option :value="2">OpenAI 兼容</option>
            </select>
          </label>
          <label class="field"><span class="field-label"><Globe2 class="field-label-icon" />目标语言</span><input id="targetLanguage" v-model="form.TargetLanguage" autocomplete="off"></label>
          <label class="field"><span class="field-label"><Gamepad2 class="field-label-icon" />游戏名称</span><input id="gameTitle" v-model="form.GameTitle" autocomplete="off" :placeholder="automaticGameTitle || '自动检测当前游戏'"></label>
          <label class="field"><span class="field-label"><MessageSquareText class="field-label-icon" />翻译风格</span>
            <select id="style" v-model.number="form.Style">
              <option :value="0">忠实</option>
              <option :value="1">自然</option>
              <option :value="2">本地化</option>
              <option :value="3">UI 简短</option>
            </select>
          </label>
        </div>
        <p class="hint">{{ activeProviderName }} · {{ activeStyleHint }}</p>
        <div v-if="!isLlamaCpp" class="ai-endpoint-grid">
          <label class="field"><span class="field-label"><Globe2 class="field-label-icon" />Base URL</span><input id="baseUrl" v-model="form.BaseUrl" autocomplete="off"></label>
          <label class="field"><span class="field-label"><Settings2 class="field-label-icon" />Endpoint</span><input id="endpoint" v-model="form.Endpoint" autocomplete="off"></label>
          <label class="field"><span class="field-label"><ListChecks class="field-label-icon" />模型预设</span>
            <select id="modelPreset" v-model="form.ModelPreset" @change="applyModelPreset">
              <option v-for="option in providerOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
          </label>
        </div>
        <div v-if="!isLlamaCpp" class="ai-model-row">
          <label class="field"><span class="field-label"><Bot class="field-label-icon" />模型</span><input id="model" v-model="form.Model" autocomplete="off" @input="updateModelPresetFromInput"></label>
          <label class="field"><span class="field-label"><KeyRound class="field-label-icon" />API Key</span><input id="apiKey" v-model="form.ApiKey" type="password" autocomplete="off" placeholder="留空不会覆盖已保存密钥"></label>
          <div class="actions inline-actions ai-provider-actions">
            <button id="saveKey" class="secondary" type="button" @click="saveKeyOnly"><FileKey class="option-icon" />只保存密钥</button>
            <button id="testProvider" class="secondary" type="button" :disabled="utilityBusy" @click="testProvider"><Zap class="button-icon" />测试连接</button>
            <button v-if="!isLlamaCpp" id="fetchModels" class="secondary" type="button" :disabled="utilityBusy" @click="fetchModels"><ListRestart class="button-icon" />获取模型列表</button>
            <button id="fetchBalance" class="secondary" type="button" :disabled="utilityBusy" @click="fetchBalance"><WalletCards class="button-icon" />查询余额/成本</button>
          </div>
        </div>
        <div v-else class="actions inline-actions ai-provider-actions">
          <button id="testProvider" class="secondary" type="button" :disabled="utilityBusy" @click="testProvider"><Zap class="button-icon" />测试连接</button>
        </div>
      </SectionPanel>

      <SectionPanel v-if="isLlamaCpp" title="llama.cpp 本地模型" :icon="Server">
        <div class="llama-local-panel">
          <div class="field llama-model-row">
            <span class="field-label"><FolderOpen class="field-label-icon" />GGUF 模型文件</span>
            <div class="input-action-row">
              <input id="llamaCppModelPath" v-model="form.LlamaCppModelPath" autocomplete="off" placeholder="选择 .gguf 模型文件">
              <button id="pickLlamaCppModel" class="secondary" type="button" :disabled="llamaCppModelPicking" @click="pickLlamaCppModel">
                <FolderOpen class="button-icon" />
                {{ llamaCppModelPicking ? "选择中..." : "选择模型" }}
              </button>
            </div>
          </div>
          <div class="llama-status-strip">
            <div><span>安装</span><strong>{{ llamaCppInstallText }}</strong></div>
            <div><span>状态</span><strong>{{ llamaCppStatus?.State ?? "stopped" }}</strong></div>
            <div class="llama-result-card"><span>结果</span><strong>{{ llamaCppStatusText }}</strong></div>
          </div>
          <div class="llama-run-row">
            <label class="field"><span class="field-label"><MessageSquareText class="field-label-icon" />上下文长度</span><input id="llamaCppContextSize" v-model.number="form.LlamaCppContextSize" type="number" min="512"></label>
            <label class="field"><span class="field-label"><Layers class="field-label-icon" />GPU 层数</span><input id="llamaCppGpuLayers" v-model.number="form.LlamaCppGpuLayers" type="number" min="0" max="999"></label>
            <label class="field"><span class="field-label"><Gauge class="field-label-icon" />并行槽位</span><input id="llamaCppParallelSlots" v-model.number="form.LlamaCppParallelSlots" type="number" min="1" max="16"></label>
            <div class="actions inline-actions llama-run-actions">
              <button v-if="!llamaCppIsActive" id="startLlamaCpp" class="primary" type="button" :disabled="llamaCppBusy" @click="startLlamaCpp"><Play class="button-icon" />{{ llamaCppBusy ? "处理中..." : "启动本地模型" }}</button>
              <button v-else id="stopLlamaCpp" class="secondary" type="button" :disabled="llamaCppBusy" @click="stopLlamaCpp"><Square class="button-icon" />{{ llamaCppBusy ? "处理中..." : "停止本地模型" }}</button>
            </div>
          </div>
          <div class="llama-tune-row">
            <label class="field"><span class="field-label"><Gauge class="field-label-icon" />Batch</span><input id="llamaCppBatchSize" v-model.number="form.LlamaCppBatchSize" type="number" min="128" max="8192"></label>
            <label class="field"><span class="field-label"><Gauge class="field-label-icon" />UBatch</span><input id="llamaCppUBatchSize" v-model.number="form.LlamaCppUBatchSize" type="number" min="64" max="4096"></label>
            <label class="field"><span class="field-label"><Zap class="field-label-icon" />Flash Attention</span>
              <select id="llamaCppFlashAttentionMode" v-model="form.LlamaCppFlashAttentionMode">
                <option value="auto">auto</option>
                <option value="on">on</option>
                <option value="off">off</option>
              </select>
            </label>
            <div class="actions inline-actions llama-run-actions">
              <button id="runLlamaCppBenchmark" class="secondary" type="button" :disabled="llamaCppBenchmarkBusy || llamaCppIsActive" @click="runLlamaCppBenchmark"><Gauge class="button-icon" />{{ llamaCppBenchmarkBusy ? "基准运行中..." : "运行 CUDA 基准" }}</button>
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
            <pre v-if="llamaCppBenchmarkResult.Errors.length || llamaCppBenchmarkResult.LastOutput">{{ [...llamaCppBenchmarkResult.Errors, llamaCppBenchmarkResult.LastOutput ?? ""].filter(Boolean).join("\n") }}</pre>
          </div>
        </div>
      </SectionPanel>

      <SectionPanel title="请求与输出" :icon="Gauge">
        <p class="hint">{{ isLlamaCpp ? "llama.cpp 使用并行槽位控制本地模型压力。" : "在线服务商最多 100 并发。" }}</p>
        <div class="form-grid four">
          <label v-if="!isLlamaCpp" class="field"><span class="field-label"><Gauge class="field-label-icon" />在线服务并发请求</span><input id="maxConcurrentRequests" v-model.number="form.MaxConcurrentRequests" type="number" min="1" max="100"></label>
          <label class="field"><span class="field-label"><Clock3 class="field-label-icon" />每分钟请求</span><input id="requestsPerMinute" v-model.number="form.RequestsPerMinute" type="number" min="1"></label>
          <label class="field"><span class="field-label"><MessageSquareText class="field-label-icon" />批次字符上限</span><input id="maxBatchCharacters" v-model.number="form.MaxBatchCharacters" type="number" min="1"></label>
          <label class="field"><span class="field-label"><Clock3 class="field-label-icon" />请求超时 (秒)</span><input id="requestTimeoutSeconds" v-model.number="form.RequestTimeoutSeconds" type="number" min="5" max="180"></label>
          <label v-if="isOpenAi" class="field provider-option" data-providers="0"><span class="field-label"><Brain class="field-label-icon" />OpenAI 推理强度</span>
            <select id="reasoningEffort" v-model="form.ReasoningEffort">
              <option value="none">关闭</option>
              <option value="low">low</option>
              <option value="medium">medium</option>
              <option value="high">high</option>
              <option value="xhigh">xhigh</option>
            </select>
          </label>
          <label v-if="isDeepSeek" class="field provider-option" data-providers="1"><span class="field-label"><Brain class="field-label-icon" />DeepSeek 推理强度</span>
            <select id="deepSeekReasoningEffort" v-model="form.DeepSeekReasoningEffort">
              <option value="none">关闭</option>
              <option value="high">high</option>
              <option value="max">max</option>
            </select>
          </label>
          <label v-if="isOpenAi" class="field provider-option" data-providers="0"><span class="field-label"><Settings2 class="field-label-icon" />OpenAI 输出详细度</span>
            <select id="outputVerbosity" v-model="form.OutputVerbosity">
              <option value="low">low</option>
              <option value="medium">medium</option>
              <option value="high">high</option>
            </select>
          </label>
          <label v-if="isDeepSeek" class="field provider-option" data-providers="1"><span class="field-label"><Brain class="field-label-icon" />DeepSeek Thinking</span>
            <select id="deepSeekThinkingMode" v-model="form.DeepSeekThinkingMode">
              <option value="disabled">关闭</option>
              <option value="enabled">启用</option>
            </select>
          </label>
          <label v-if="canUseTemperature" class="field provider-option" data-providers="1,2,3"><span class="field-label"><Thermometer class="field-label-icon" />Temperature</span><input id="temperature" v-model="form.Temperature" type="number" min="0" max="2" step="0.1"></label>
        </div>
      </SectionPanel>

      <SectionPanel title="提示词" :icon="MessageSquareText">
        <div class="prompt-editor-head">
          <span class="prompt-mode">{{ promptModeText }}</span>
          <div class="actions inline-actions prompt-editor-actions">
            <button id="restorePromptTemplate" class="secondary" type="button" :disabled="promptTemplateUsesDefault(activePromptTemplateKey)" @click="restoreDefaultPrompt"><RotateCcw class="button-icon" />恢复当前模板</button>
            <button id="restoreAllPromptTemplates" class="secondary" type="button" :disabled="promptUsesDefault" @click="restoreAllPromptTemplates"><RotateCcw class="button-icon" />{{ restorePromptText }}</button>
          </div>
        </div>
        <div class="prompt-template-tabs" role="tablist" aria-label="提示词模板">
          <button
            v-for="field in promptTemplateFields"
            :key="field.key"
            class="prompt-template-tab"
            :class="{ active: activePromptTemplateKey === field.key }"
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
  </section>
</template>
