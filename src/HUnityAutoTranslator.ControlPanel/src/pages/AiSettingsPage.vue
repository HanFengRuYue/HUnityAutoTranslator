<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { api } from "../api/client";
import SectionPanel from "../components/SectionPanel.vue";
import {
  controlPanelStore,
  saveApiKey,
  saveConfig,
  setDirtyForm,
  showToast
} from "../state/controlPanelStore";
import type {
  ControlPanelState,
  ProviderBalanceInfo,
  ProviderBalanceResult,
  ProviderModelInfo,
  ProviderModelsResult,
  ProviderTestResult,
  UpdateConfigRequest
} from "../types/api";

const formKey = "ai";

const providerNames: Record<number, string> = {
  0: "OpenAI",
  1: "DeepSeek",
  2: "OpenAI 兼容"
};

const providerDefaults: Record<number, { baseUrl: string; endpoint: string; model: string }> = {
  0: { baseUrl: "https://api.openai.com", endpoint: "/v1/responses", model: "gpt-5.5" },
  1: { baseUrl: "https://api.deepseek.com", endpoint: "/chat/completions", model: "deepseek-v4-flash" },
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
  ReasoningEffort: "low",
  DeepSeekReasoningEffort: "high",
  OutputVerbosity: "low",
  DeepSeekThinkingMode: "enabled",
  Temperature: "",
  CustomInstruction: "",
  CustomPrompt: ""
});

const providerModels = ref<ProviderModelInfo[]>([]);
const balances = ref<ProviderBalanceInfo[]>([]);
const utilityBusy = ref(false);
const formDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));
const providerOptions = computed(() => modelPresets[form.ProviderKind] ?? modelPresets[0]);
const activeProviderName = computed(() => providerNames[form.ProviderKind] ?? "-");
const activeStyleHint = computed(() => styleHints[form.Style] ?? "");
const isOpenAi = computed(() => form.ProviderKind === 0);
const isDeepSeek = computed(() => form.ProviderKind === 1);
const canUseTemperature = computed(() => form.ProviderKind === 1 || form.ProviderKind === 2);

function markDirty(): void {
  setDirtyForm(formKey, true);
}

function numberValue(value: number | string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

function normalizeDeepSeekEffort(value: unknown): string {
  const normalized = String(value ?? "").trim().toLowerCase();
  if (normalized === "max" || normalized === "xhigh") {
    return "max";
  }

  return "high";
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
  form.Style = numberValue(state.Style);
  form.ProviderKind = numberValue(state.ProviderKind);
  form.BaseUrl = state.BaseUrl;
  form.Endpoint = state.Endpoint;
  form.Model = state.Model;
  form.RequestTimeoutSeconds = state.RequestTimeoutSeconds;
  form.MaxConcurrentRequests = state.MaxConcurrentRequests;
  form.RequestsPerMinute = state.RequestsPerMinute;
  form.MaxBatchCharacters = state.MaxBatchCharacters;
  form.ReasoningEffort = state.ReasoningEffort;
  form.DeepSeekReasoningEffort = normalizeDeepSeekEffort(state.ReasoningEffort);
  form.OutputVerbosity = state.OutputVerbosity;
  form.DeepSeekThinkingMode = state.DeepSeekThinkingMode;
  form.Temperature = state.Temperature === null || state.Temperature === undefined ? "" : String(state.Temperature);
  form.CustomInstruction = state.CustomInstruction ?? "";
  form.CustomPrompt = state.CustomPrompt ?? "";
  form.ApiKey = "";
  updateModelPresetFromInput();
  setDirtyForm(formKey, false);
}

function activeReasoningEffort(): string {
  return form.ProviderKind === 1 ? form.DeepSeekReasoningEffort : form.ReasoningEffort;
}

function readConfig(): UpdateConfigRequest {
  const temperatureText = form.Temperature.trim();
  return {
    TargetLanguage: form.TargetLanguage,
    Style: numberValue(form.Style),
    ProviderKind: numberValue(form.ProviderKind),
    BaseUrl: form.BaseUrl,
    Endpoint: form.Endpoint,
    Model: form.Model,
    RequestTimeoutSeconds: numberValue(form.RequestTimeoutSeconds),
    MaxConcurrentRequests: numberValue(form.MaxConcurrentRequests),
    RequestsPerMinute: numberValue(form.RequestsPerMinute),
    MaxBatchCharacters: numberValue(form.MaxBatchCharacters),
    ReasoningEffort: activeReasoningEffort(),
    OutputVerbosity: form.OutputVerbosity,
    DeepSeekThinkingMode: form.DeepSeekThinkingMode,
    Temperature: temperatureText === "" ? null : Number(temperatureText),
    ClearTemperature: canUseTemperature.value && temperatureText === "",
    CustomInstruction: form.CustomInstruction,
    CustomPrompt: form.CustomPrompt
  };
}

async function saveConfigOnly(): Promise<ControlPanelState | null> {
  const state = await saveConfig(readConfig(), formKey);
  applyState(state, true);
  return state;
}

async function savePendingApiKey(): Promise<boolean> {
  const apiKey = form.ApiKey.trim();
  if (!apiKey) {
    return false;
  }

  const state = await saveApiKey(apiKey);
  if (state) {
    form.ApiKey = "";
    applyState(state, true);
    return true;
  }

  return false;
}

async function saveAll(): Promise<void> {
  await saveConfigOnly();
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
    await saveConfigOnly();
    await savePendingApiKey();
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
    showToast(result.Succeeded ? "连接可用。" : (result.Message || "连接失败。"), result.Succeeded ? "ok" : "error");
  });
}

async function fetchModels(): Promise<void> {
  await runProviderUtility("获取模型列表", async () => {
    const result = await api<ProviderModelsResult>("/api/provider/models");
    providerModels.value = result.Models;
    showToast(`已获取 ${result.Models.length} 个模型。${result.Message ? ` ${result.Message}` : ""}`, result.Succeeded ? "ok" : "warn");
  });
}

async function fetchBalance(): Promise<void> {
  await runProviderUtility("查询余额/成本", async () => {
    const result = await api<ProviderBalanceResult>("/api/provider/balance");
    balances.value = result.Balances;
    showToast(result.Message || `已获取 ${result.Balances.length} 条余额记录。`, result.Succeeded ? "ok" : "warn");
  });
}

function applyProviderDefaults(): void {
  const defaults = providerDefaults[form.ProviderKind] ?? providerDefaults[0];
  form.BaseUrl = defaults.baseUrl;
  form.Endpoint = defaults.endpoint;
  form.Model = defaults.model;
  updateModelPresetFromInput();
  markDirty();
}

function applyModelPreset(): void {
  if (form.ModelPreset !== "custom") {
    form.Model = form.ModelPreset;
    markDirty();
  }
}

watch(() => controlPanelStore.state, (state) => applyState(state), { immediate: true });
</script>

<template>
  <section class="page active" id="page-ai">
    <div class="page-head">
      <div>
        <h1>AI 翻译设置</h1>
        <p>配置服务商、模型、密钥和 Prompt，操作反馈显示在顶部提示中。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="!formDirty" @click="applyState(controlPanelStore.state, true)">还原</button>
        <button class="primary" type="button" @click="saveAll">保存 AI 设置</button>
      </div>
    </div>

    <div class="form-stack" @input="markDirty" @change="markDirty">
      <SectionPanel title="服务商">
        <div class="form-grid three">
          <label class="field"><span>服务商</span>
            <select id="providerKind" v-model.number="form.ProviderKind" @change="applyProviderDefaults">
              <option :value="0">OpenAI Responses</option>
              <option :value="1">DeepSeek</option>
              <option :value="2">OpenAI 兼容</option>
            </select>
          </label>
          <label class="field"><span>目标语言</span><input id="targetLanguage" v-model="form.TargetLanguage" autocomplete="off"></label>
          <label class="field"><span>翻译风格</span>
            <select id="style" v-model.number="form.Style">
              <option :value="0">忠实</option>
              <option :value="1">自然</option>
              <option :value="2">本地化</option>
              <option :value="3">UI 简短</option>
            </select>
          </label>
        </div>
        <p class="hint">{{ activeProviderName }} · {{ activeStyleHint }}</p>
        <div class="form-grid three">
          <label class="field"><span>Base URL</span><input id="baseUrl" v-model="form.BaseUrl" autocomplete="off"></label>
          <label class="field"><span>Endpoint</span><input id="endpoint" v-model="form.Endpoint" autocomplete="off"></label>
          <label class="field"><span>模型预设</span>
            <select id="modelPreset" v-model="form.ModelPreset" @change="applyModelPreset">
              <option v-for="option in providerOptions" :key="option.value" :value="option.value">{{ option.label }}</option>
            </select>
          </label>
        </div>
        <div class="form-grid two">
          <label class="field"><span>模型</span><input id="model" v-model="form.Model" autocomplete="off" @input="updateModelPresetFromInput"></label>
          <label class="field"><span>API Key</span><input id="apiKey" v-model="form.ApiKey" type="password" autocomplete="off" placeholder="留空不会覆盖已保存密钥"></label>
        </div>
        <div class="actions inline-actions">
          <button id="saveKey" class="secondary" type="button" @click="saveKeyOnly">只保存密钥</button>
          <button id="testProvider" class="secondary" type="button" :disabled="utilityBusy" @click="testProvider">测试连接</button>
          <button id="fetchModels" class="secondary" type="button" :disabled="utilityBusy" @click="fetchModels">获取模型列表</button>
          <button id="fetchBalance" class="secondary" type="button" :disabled="utilityBusy" @click="fetchBalance">查询余额/成本</button>
        </div>
      </SectionPanel>

      <SectionPanel title="请求与输出">
        <div class="form-grid four">
          <label class="field"><span>并发请求</span><input id="maxConcurrentRequests" v-model.number="form.MaxConcurrentRequests" type="number" min="1"></label>
          <label class="field"><span>每分钟请求</span><input id="requestsPerMinute" v-model.number="form.RequestsPerMinute" type="number" min="1"></label>
          <label class="field"><span>批次字符上限</span><input id="maxBatchCharacters" v-model.number="form.MaxBatchCharacters" type="number" min="1"></label>
          <label class="field"><span>请求超时 (秒)</span><input id="requestTimeoutSeconds" v-model.number="form.RequestTimeoutSeconds" type="number" min="5" max="180"></label>
          <label v-if="isOpenAi" class="field provider-option" data-providers="0"><span>OpenAI 推理强度</span>
            <select id="reasoningEffort" v-model="form.ReasoningEffort">
              <option value="none">none</option>
              <option value="low">low</option>
              <option value="medium">medium</option>
              <option value="high">high</option>
              <option value="xhigh">xhigh</option>
            </select>
          </label>
          <label v-if="isDeepSeek" class="field provider-option" data-providers="1"><span>DeepSeek 推理强度</span>
            <select id="deepSeekReasoningEffort" v-model="form.DeepSeekReasoningEffort">
              <option value="high">high</option>
              <option value="max">max</option>
            </select>
          </label>
          <label v-if="isOpenAi" class="field provider-option" data-providers="0"><span>OpenAI 输出详细度</span>
            <select id="outputVerbosity" v-model="form.OutputVerbosity">
              <option value="low">low</option>
              <option value="medium">medium</option>
              <option value="high">high</option>
            </select>
          </label>
          <label v-if="isDeepSeek" class="field provider-option" data-providers="1"><span>DeepSeek Thinking</span>
            <select id="deepSeekThinkingMode" v-model="form.DeepSeekThinkingMode">
              <option value="enabled">启用</option>
              <option value="disabled">关闭</option>
            </select>
          </label>
          <label v-if="canUseTemperature" class="field provider-option" data-providers="1,2"><span>Temperature</span><input id="temperature" v-model="form.Temperature" type="number" min="0" max="2" step="0.1"></label>
        </div>
      </SectionPanel>

      <SectionPanel title="Prompt">
        <label class="field"><span>自定义指令</span><textarea id="customInstruction" v-model="form.CustomInstruction" rows="4" spellcheck="false"></textarea></label>
        <label class="field"><span>自定义完整提示词</span><textarea id="customPrompt" v-model="form.CustomPrompt" rows="8" spellcheck="false"></textarea></label>
      </SectionPanel>

      <SectionPanel title="服务商返回">
        <div class="utility-results">
          <div>
            <h3>模型列表</h3>
            <div v-if="providerModels.length" class="utility-list">
              <button v-for="model in providerModels" :key="model.Id" type="button" @click="form.Model = model.Id; updateModelPresetFromInput(); markDirty()">
                <span>{{ model.Id }}</span>
                <small>{{ model.OwnedBy ?? "未知来源" }}</small>
              </button>
            </div>
            <div v-else class="empty-state compact">尚未获取模型</div>
          </div>
          <div>
            <h3>余额/成本</h3>
            <div v-if="balances.length" class="utility-list">
              <div v-for="balance in balances" :key="`${balance.Currency}-${balance.TotalBalance}`" class="utility-row">
                <span>{{ balance.Currency }}</span>
                <strong>{{ balance.TotalBalance }}</strong>
                <small v-if="balance.GrantedBalance">赠送 {{ balance.GrantedBalance }}</small>
              </div>
            </div>
            <div v-else class="empty-state compact">尚未查询余额</div>
          </div>
        </div>
      </SectionPanel>
    </div>
  </section>
</template>
