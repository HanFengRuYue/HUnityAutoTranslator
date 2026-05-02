<script setup lang="ts">
import { computed } from "vue";
import {
  Activity,
  AlertTriangle,
  Bot,
  CheckCircle2,
  Coins,
  Database,
  History,
  Info,
  ListTodo,
  LoaderPinwheel,
  RefreshCw,
  ShieldCheck,
  XCircle
} from "lucide-vue-next";
import MetricCard from "../components/MetricCard.vue";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, refreshState, runSelfCheck } from "../state/controlPanelStore";
import type { SelfCheckItem, SelfCheckSeverity } from "../types/api";
import { formatDateTime, formatNumber, formatRate } from "../utils/format";

const providerNames: Record<string, string> = {
  "0": "OpenAI",
  OpenAI: "OpenAI",
  "1": "DeepSeek",
  DeepSeek: "DeepSeek",
  "2": "OpenAI 兼容",
  OpenAICompatible: "OpenAI 兼容",
  "3": "llama.cpp",
  LlamaCpp: "llama.cpp"
};

const state = computed(() => controlPanelStore.state);
const isOffline = computed(() => controlPanelStore.connection === "offline");
const providerProfiles = computed(() => state.value?.ProviderProfiles ?? []);
const recentTranslations = computed(() => state.value?.RecentTranslations ?? []);
const selfCheckReport = computed(() => state.value?.SelfCheck ?? null);
const selfCheckItems = computed(() => selfCheckReport.value?.Items ?? []);

const enabledText = computed(() => {
  if (isOffline.value) {
    return "连接中断";
  }

  if (!state.value) {
    return "正在连接";
  }

  return state.value.Enabled ? "运行中" : "已暂停";
});

const enabledTone = computed(() => {
  if (isOffline.value) {
    return "danger";
  }

  return state.value?.Enabled ? "ok" : "warn";
});

const queueCount = computed(() => state.value ? state.value.QueueCount ?? state.value.QueuedTextCount ?? 0 : 0);
const activeTranslationCapacity = computed(() => {
  const inFlight = state.value ? Math.max(0, state.value.InFlightTranslationCount ?? 0) : 0;
  const max = state.value ? Math.max(1, state.value.EffectiveMaxConcurrentRequests ?? 1) : 0;
  return { inFlight, max };
});

const activeProviderProfileLabel = computed(() => {
  if (!state.value) {
    return "-";
  }

  return providerProfiles.value.length ? state.value.ActiveProviderProfileName ?? "无可用配置" : "未配置";
});

const activeProviderKindLabel = computed(() => {
  if (!providerProfiles.value.length) {
    return "未配置";
  }

  const kind = state.value?.ActiveTranslationProvider?.Kind ?? state.value?.ActiveProviderProfileKind;
  return providerNames[String(kind ?? "")] ?? "-";
});

const activeProviderModelLabel = computed(() => {
  const runtimeModel = state.value?.ActiveTranslationProvider?.Model;
  if (runtimeModel) {
    return runtimeModel;
  }

  const profileModel = state.value?.ActiveProviderProfileModel;
  if (profileModel) {
    return profileModel;
  }

  if (!providerProfiles.value.length || !state.value?.ActiveProviderProfileId) {
    return activeProviderProfileLabel.value;
  }

  return "-";
});

const selfCheckStatusText = computed(() => {
  const report = selfCheckReport.value;
  if (!report) {
    return "尚未运行";
  }

  const stateName = runStateName(report.State);
  if (stateName === "Running") {
    return "自检中";
  }

  if (stateName === "Failed") {
    return "自检异常";
  }

  const severity = severityName(report.Severity);
  if (severity === "Error") {
    return "发现异常";
  }

  if (severity === "Warning") {
    return "需要注意";
  }

  return "未发现阻断问题";
});

const selfCheckTone = computed(() => {
  const report = selfCheckReport.value;
  if (!report) {
    return "warn";
  }

  const stateName = runStateName(report.State);
  if (stateName === "Running") {
    return "warn";
  }

  if (stateName === "Failed" || severityName(report.Severity) === "Error") {
    return "danger";
  }

  return severityName(report.Severity) === "Warning" ? "warn" : "ok";
});

const selfCheckGroups = computed(() => {
  const groups = new Map<string, SelfCheckItem[]>();
  for (const item of selfCheckItems.value) {
    const items = groups.get(item.Category) ?? [];
    items.push(item);
    groups.set(item.Category, items);
  }

  return Array.from(groups, ([category, items]) => ({ category, items }));
});

const isSelfCheckRunning = computed(() => runStateName(selfCheckReport.value?.State) === "Running");

function formatInFlightCapacity(inFlight: number, max: number): string {
  return `${formatNumber(inFlight)}/${formatNumber(max)}`;
}

function formatRecentProvider(item: { Provider: string; Model: string; ProviderProfileName: string | null }): string {
  return `${item.ProviderProfileName ?? item.Provider} / ${item.Model}`;
}

function severityName(value: string | number | null | undefined): string {
  if (typeof value === "number") {
    return ["Ok", "Info", "Warning", "Error", "Skipped"][value] ?? String(value);
  }

  return value ?? "";
}

function runStateName(value: string | number | null | undefined): string {
  if (typeof value === "number") {
    return ["NotStarted", "Running", "Completed", "Failed"][value] ?? String(value);
  }

  return value ?? "";
}

function runStateText(value: string | number | null | undefined): string {
  const stateName = runStateName(value);
  if (stateName === "Running" || stateName === "1") {
    return "运行中";
  }

  if (stateName === "Completed" || stateName === "2") {
    return "已完成";
  }

  if (stateName === "Failed" || stateName === "3") {
    return "异常中止";
  }

  return "未运行";
}

function severityText(value: SelfCheckSeverity): string {
  const severity = severityName(value);
  if (severity === "Error" || severity === "3") {
    return "异常";
  }

  if (severity === "Warning" || severity === "2") {
    return "警告";
  }

  if (severity === "Skipped" || severity === "4") {
    return "跳过";
  }

  if (severity === "Info" || severity === "1") {
    return "提示";
  }

  return "正常";
}

function severityTone(value: SelfCheckSeverity): "ok" | "warn" | "danger" | "info" {
  const severity = severityName(value);
  if (severity === "Error" || severity === "3") {
    return "danger";
  }

  if (severity === "Warning" || severity === "2") {
    return "warn";
  }

  if (severity === "Info" || severity === "Skipped" || severity === "1" || severity === "4") {
    return "info";
  }

  return "ok";
}

function selfCheckSeverityIcon(value: SelfCheckSeverity) {
  const tone = severityTone(value);
  if (tone === "danger") {
    return XCircle;
  }

  if (tone === "warn") {
    return AlertTriangle;
  }

  if (tone === "info") {
    return Info;
  }

  return CheckCircle2;
}
</script>

<template>
  <section class="page active" id="page-status">
    <div class="page-head">
      <div>
        <h1>运行状态</h1>
        <p>查看插件连接、翻译队列、缓存和 AI 服务。</p>
      </div>
      <button class="secondary" type="button" :disabled="controlPanelStore.isRefreshing" @click="refreshState()">
        <RefreshCw class="button-icon" />
        {{ controlPanelStore.isRefreshing ? "刷新中" : "刷新" }}
      </button>
    </div>

    <div class="status-layout">
      <div class="metric-grid" aria-label="运行指标">
        <MetricCard
          label="插件状态"
          :value="enabledText"
          value-id="enabledText"
          :icon="Activity"
          :tone="enabledTone"
          help="当前插件是否启用；连接中断时会显示离线状态。"
        />
        <MetricCard
          label="等待翻译"
          :value="formatNumber(queueCount)"
          value-id="queueCount"
          :icon="ListTodo"
          tone="warn"
          help="已经排队、尚未被 AI 服务处理的文本数量。"
        />
        <MetricCard
          label="正在翻译"
          :value="formatInFlightCapacity(activeTranslationCapacity.inFlight, activeTranslationCapacity.max)"
          value-id="inFlightTranslationCount"
          :icon="LoaderPinwheel"
          tone="warn"
          help="当前正在占用的 AI 请求/槽位数，以及 AI 设置中的最大并发上限；同一请求可能批量包含多条文本。"
        />
        <MetricCard
          label="已翻译文本"
          :value="formatNumber(state?.CacheCount)"
          value-id="cacheCount"
          :icon="Database"
          tone="ok"
          help="已经保存到本地 SQLite，后续可直接复用或编辑的译文数量。"
        />
        <MetricCard
          label="预计token用量"
          :value="formatNumber(state?.TotalTokenCount)"
          value-id="totalTokenCount"
          :icon="Coins"
          help="服务商返回的累计 token 用量，用于粗略判断成本。"
        />
      </div>

      <SectionPanel class="self-check-panel" title="本地自检" :icon="ShieldCheck">
        <template #actions>
          <button class="secondary" type="button" :disabled="isSelfCheckRunning" @click="runSelfCheck()">
            <RefreshCw class="button-icon" />
            {{ isSelfCheckRunning ? "自检中..." : "重新自检" }}
          </button>
        </template>
        <div class="self-check-summary">
          <div>
            <span>总状态</span>
            <strong :class="`status-${selfCheckTone}`">{{ selfCheckStatusText }}</strong>
          </div>
          <div>
            <span>错误 / 警告</span>
            <strong>{{ formatNumber(selfCheckReport?.ErrorCount) }} / {{ formatNumber(selfCheckReport?.WarningCount) }}</strong>
          </div>
          <div>
            <span>跳过项</span>
            <strong>{{ formatNumber(selfCheckReport?.SkippedCount) }}</strong>
          </div>
          <div>
            <span>最近运行</span>
            <strong>{{ selfCheckReport?.CompletedUtc ? formatDateTime(selfCheckReport.CompletedUtc) : runStateText(selfCheckReport?.State) }}</strong>
          </div>
        </div>
        <div v-if="selfCheckGroups.length" class="self-check-list">
          <details v-for="group in selfCheckGroups" :key="group.category" class="self-check-group" open>
            <summary>
              <span>{{ group.category }}</span>
              <strong>{{ formatNumber(group.items.length) }}</strong>
            </summary>
            <article
              v-for="item in group.items"
              :key="item.Id"
              class="self-check-item"
              :class="`self-check-${severityTone(item.Severity)}`"
            >
              <component :is="selfCheckSeverityIcon(item.Severity)" class="self-check-item-icon" />
              <div>
                <div class="self-check-item-head">
                  <strong>{{ item.Name }}</strong>
                  <span>{{ severityText(item.Severity) }}</span>
                </div>
                <p>{{ item.Evidence }}</p>
                <small>{{ item.Recommendation }}</small>
              </div>
            </article>
          </details>
        </div>
        <div v-else class="empty-state compact">暂无自检结果</div>
      </SectionPanel>

      <SectionPanel title="AI 服务" :icon="Bot">
        <div class="provider-summary provider-summary-compact">
          <div>
            <span>当前配置</span>
            <strong>{{ activeProviderProfileLabel }}</strong>
          </div>
          <div>
            <span>服务商</span>
            <strong>{{ activeProviderKindLabel }}</strong>
          </div>
          <div>
            <span>模型</span>
            <strong>{{ activeProviderModelLabel }}</strong>
          </div>
          <div>
            <span>处理速度</span>
            <strong>{{ formatRate(state?.AverageCharactersPerSecond) }}</strong>
          </div>
        </div>
      </SectionPanel>

      <SectionPanel title="最近完成" :icon="History">
        <div v-if="recentTranslations.length" class="recent-list">
          <article v-for="item in recentTranslations" :key="`${item.SourceText}-${item.CompletedUtc}`" class="recent-item">
            <div>
              <span>{{ item.TargetLanguage }} · {{ formatRecentProvider(item) }}</span>
              <strong>{{ item.TranslatedText }}</strong>
              <p>{{ item.SourceText }}</p>
            </div>
            <time>{{ formatDateTime(item.CompletedUtc) }}</time>
          </article>
        </div>
        <div v-else class="empty-state">暂无完成记录</div>
      </SectionPanel>
    </div>
  </section>
</template>
