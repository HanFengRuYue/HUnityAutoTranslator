<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  Activity,
  AlertTriangle,
  Bot,
  ChevronDown,
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

type SelfCheckTone = "ok" | "warn" | "danger" | "info";

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
  const groups = new Map<string, { items: SelfCheckItem[]; order: number }>();
  for (const [index, item] of selfCheckItems.value.entries()) {
    const group = groups.get(item.Category) ?? { items: [], order: index };
    group.items.push(item);
    groups.set(item.Category, group);
  }

  return Array.from(groups, ([category, group]) => {
    const items = [...group.items].sort(compareSelfCheckItems);
    return {
      category,
      items,
      order: group.order,
      tone: groupTone(items),
      attentionCount: items.filter(requiresAttention).length
    };
  }).sort(compareSelfCheckGroups);
});

const isSelfCheckRunning = computed(() => runStateName(selfCheckReport.value?.State) === "Running");
const selfCheckPanelOpen = ref(false);
const selfCheckHasProblem = computed(() => {
  const report = selfCheckReport.value;
  if (!report) {
    return false;
  }

  const stateName = runStateName(report.State);
  const severity = severityName(report.Severity);
  return stateName === "Failed" || severity === "Error" || severity === "Warning";
});

watch(selfCheckHasProblem, (hasProblem) => {
  selfCheckPanelOpen.value = hasProblem;
}, { immediate: true });

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

function severityTone(value: SelfCheckSeverity): SelfCheckTone {
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

function severitySortRank(value: SelfCheckSeverity): number {
  const severity = severityName(value);
  if (severity === "Error" || severity === "3") {
    return 0;
  }

  if (severity === "Warning" || severity === "2") {
    return 1;
  }

  if (severity === "Info" || severity === "1") {
    return 2;
  }

  if (severity === "Skipped" || severity === "4") {
    return 3;
  }

  return 4;
}

function groupSortRank(items: SelfCheckItem[]): number {
  const ranks = items.map((item) => severitySortRank(item.Severity));
  const highestRank = ranks.length ? Math.min(...ranks) : 4;
  return highestRank <= 1 ? highestRank : 2;
}

function compareSelfCheckItems(first: SelfCheckItem, second: SelfCheckItem): number {
  return severitySortRank(first.Severity) - severitySortRank(second.Severity)
    || first.Name.localeCompare(second.Name, "zh-Hans-CN")
    || first.Id.localeCompare(second.Id, "zh-Hans-CN");
}

function compareSelfCheckGroups(
  first: { items: SelfCheckItem[]; attentionCount: number; order: number },
  second: { items: SelfCheckItem[]; attentionCount: number; order: number }
): number {
  return groupSortRank(first.items) - groupSortRank(second.items)
    || second.attentionCount - first.attentionCount
    || first.order - second.order;
}

function requiresAttention(item: SelfCheckItem): boolean {
  return severitySortRank(item.Severity) <= 1;
}

function groupTone(items: SelfCheckItem[]): SelfCheckTone {
  if (items.some((item) => severityTone(item.Severity) === "danger")) {
    return "danger";
  }

  if (items.some((item) => severityTone(item.Severity) === "warn")) {
    return "warn";
  }

  if (items.some((item) => severityTone(item.Severity) === "info")) {
    return "info";
  }

  return "ok";
}

function selfCheckSeverityIcon(value: SelfCheckSeverity) {
  const tone = severityTone(value);
  return selfCheckToneIcon(tone);
}

function selfCheckToneIcon(tone: SelfCheckTone) {
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

function syncSelfCheckPanelOpen(event: Event): void {
  selfCheckPanelOpen.value = (event.currentTarget as HTMLDetailsElement).open;
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

      <details class="section-panel self-check-panel self-check-panel-collapsible" aria-labelledby="section-本地自检" :open="selfCheckPanelOpen" @toggle="syncSelfCheckPanelOpen">
        <summary class="section-head self-check-panel-toggle">
          <div>
            <div class="section-title-row">
              <ShieldCheck class="section-icon" aria-hidden="true" />
              <h2 id="section-本地自检">本地自检</h2>
            </div>
            <div class="self-check-title-meta">
              <strong :class="`status-${selfCheckTone}`">{{ selfCheckStatusText }}</strong>
              <span>{{ formatNumber(selfCheckReport?.ErrorCount) }} 错误 · {{ formatNumber(selfCheckReport?.WarningCount) }} 警告</span>
            </div>
          </div>
          <div class="section-actions self-check-header-actions">
            <span class="self-check-toggle-button" :title="selfCheckPanelOpen ? '收起本地自检明细' : '展开本地自检明细'">
              <ChevronDown class="self-check-toggle-icon" aria-hidden="true" />
              <span>{{ selfCheckPanelOpen ? "收起明细" : "展开明细" }}</span>
            </span>
            <button class="secondary" type="button" :disabled="isSelfCheckRunning" @click.stop.prevent="runSelfCheck()">
              <RefreshCw class="button-icon" />
              {{ isSelfCheckRunning ? "自检中..." : "重新自检" }}
            </button>
          </div>
        </summary>
        <div class="self-check-panel-body">
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
            <details
              v-for="group in selfCheckGroups"
              :key="group.category"
              class="self-check-group"
              :class="`self-check-group-${group.tone}`"
              open
            >
              <summary class="self-check-group-summary">
                <span class="self-check-group-title">
                  <ChevronDown class="self-check-group-chevron" aria-hidden="true" />
                  <component :is="selfCheckToneIcon(group.tone)" class="self-check-group-icon" />
                  <span>{{ group.category }}</span>
                </span>
                <span class="self-check-group-counts">
                  <strong v-if="group.attentionCount" class="self-check-attention-count">{{ formatNumber(group.attentionCount) }} 项需处理</strong>
                  <strong>{{ formatNumber(group.items.length) }}</strong>
                </span>
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
        </div>
      </details>

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
