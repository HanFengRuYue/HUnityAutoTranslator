<script setup lang="ts">
import { computed } from "vue";
import {
  Activity,
  Bot,
  Coins,
  Database,
  Gauge,
  History,
  ListTodo,
  LoaderCircle,
  RefreshCw,
  Timer
} from "lucide-vue-next";
import MetricCard from "../components/MetricCard.vue";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, refreshState } from "../state/controlPanelStore";
import { formatDateTime, formatMilliseconds, formatNumber, formatRate } from "../utils/format";

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
  const max = state.value ? Math.max(1, state.value.MaxConcurrentRequests ?? 1) : 0;
  return { inFlight, max };
});
const providerLabel = computed(() => providerNames[String(state.value?.ProviderKind ?? "")] ?? "-");
const recentTranslations = computed(() => state.value?.RecentTranslations ?? []);

function formatInFlightCapacity(inFlight: number, max: number): string {
  return `${formatNumber(inFlight)}/${formatNumber(max)}`;
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
          help="当前插件是否启用，连接中断时会显示离线状态。"
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
          :icon="LoaderCircle"
          tone="warn"
          help="当前正在处理的翻译数量，以及 AI 设置中的最大并发上限。"
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
          label="平均耗时"
          :value="formatMilliseconds(state?.AverageTranslationMilliseconds)"
          value-id="averageTranslationMilliseconds"
          :icon="Timer"
          help="有耗时记录的翻译请求平均完成时间。"
        />
        <MetricCard
          label="处理速度"
          :value="formatRate(state?.AverageCharactersPerSecond)"
          value-id="averageCharactersPerSecond"
          :icon="Gauge"
          help="按源文本字符数估算的平均翻译吞吐。"
        />
        <MetricCard
          label="令牌用量"
          :value="formatNumber(state?.TotalTokenCount)"
          value-id="totalTokenCount"
          :icon="Coins"
          help="服务商返回的累计 token 用量，用于粗略判断成本。"
        />
      </div>

      <SectionPanel title="AI 服务" :icon="Bot">
        <div class="provider-summary provider-summary-compact">
          <div>
            <span>服务商</span>
            <strong>{{ providerLabel }}</strong>
          </div>
          <div>
            <span>模型</span>
            <strong>{{ state?.Model ?? "-" }}</strong>
          </div>
          <div>
            <span>API Key</span>
            <strong>{{ state?.ApiKeyConfigured ? (state.ApiKeyPreview ?? "已配置") : "未配置" }}</strong>
          </div>
        </div>
      </SectionPanel>

      <SectionPanel title="最近完成" :icon="History">
        <div v-if="recentTranslations.length" class="recent-list">
          <article v-for="item in recentTranslations" :key="`${item.SourceText}-${item.CompletedUtc}`" class="recent-item">
            <div>
              <span>{{ item.TargetLanguage }} · {{ item.Provider }} / {{ item.Model }}</span>
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
