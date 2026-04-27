<script setup lang="ts">
import { computed } from "vue";
import MetricCard from "../components/MetricCard.vue";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, refreshState } from "../state/controlPanelStore";
import { formatDateTime, formatMilliseconds, formatNumber, formatRate } from "../utils/format";

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
const providerStatusText = computed(() => {
  if (isOffline.value) {
    return "无法连接本机面板";
  }

  const providerStatus = state.value?.ProviderStatus;
  if (!providerStatus?.Message) {
    return state.value?.ApiKeyConfigured ? "服务待检测" : "等待填写密钥";
  }

  return providerStatus.Message;
});

const providerTone = computed(() => {
  const providerState = state.value?.ProviderStatus?.State?.toLowerCase();
  if (isOffline.value || providerState === "error") {
    return "danger";
  }

  if (providerState === "ok") {
    return "ok";
  }

  return "warn";
});

const recentTranslations = computed(() => state.value?.RecentTranslations ?? []);
</script>

<template>
  <section class="page active" id="page-status">
    <div class="page-head">
      <div>
        <h1>运行状态</h1>
        <p>查看插件连接、翻译队列、缓存和 AI 服务状态。</p>
      </div>
      <button class="secondary" type="button" :disabled="controlPanelStore.isRefreshing" @click="refreshState()">
        {{ controlPanelStore.isRefreshing ? "刷新中" : "刷新" }}
      </button>
    </div>

    <div class="status-layout">
      <div class="status-command" aria-label="当前运行概览">
        <div class="status-command-main">
          <span class="eyebrow">实时运行</span>
          <strong :class="`status-${enabledTone}`">{{ enabledText }}</strong>
          <p>{{ providerStatusText }}</p>
        </div>
        <div class="status-command-grid">
          <div>
            <span>等待翻译</span>
            <strong>{{ formatNumber(queueCount) }}</strong>
          </div>
          <div>
            <span>写回等待</span>
            <strong>{{ formatNumber(state?.WritebackQueueCount) }}</strong>
          </div>
          <div>
            <span>本地译文</span>
            <strong>{{ formatNumber(state?.CacheCount) }}</strong>
          </div>
        </div>
      </div>

      <div class="metric-grid" aria-label="运行指标">
        <MetricCard
          label="插件状态"
          :value="enabledText"
          value-id="enabledText"
          :tone="enabledTone"
          help="当前插件是否启用。连接中断时这里会立即变为离线状态，避免留下过期的运行中提示。"
        />
        <MetricCard
          label="等待翻译"
          :value="formatNumber(queueCount)"
          value-id="queueCount"
          tone="warn"
          help="已经排队、尚未被 AI 服务处理的文本数量。0 也会准确显示，方便判断队列是否清空。"
        />
        <MetricCard
          label="写回等待"
          :value="formatNumber(state?.WritebackQueueCount)"
          value-id="writebackQueueCount"
          help="译文已准备好，等待写回 Unity 文本组件的数量。"
        />
        <MetricCard
          label="已翻译文本"
          :value="formatNumber(state?.CacheCount)"
          value-id="cacheCount"
          tone="ok"
          help="已经保存到本地 SQLite，后续可直接复用或编辑的译文数量。"
        />
        <MetricCard
          label="完成次数"
          :value="formatNumber(state?.CompletedTranslationCount)"
          value-id="completedTranslationCount"
          help="本次运行中 AI 翻译完成的文本次数。"
        />
        <MetricCard
          label="平均耗时"
          :value="formatMilliseconds(state?.AverageTranslationMilliseconds)"
          value-id="averageTranslationMilliseconds"
          help="有耗时记录的翻译请求平均完成时间。"
        />
        <MetricCard
          label="处理速度"
          :value="formatRate(state?.AverageCharactersPerSecond)"
          value-id="averageCharactersPerSecond"
          help="按源文本字符数估算的平均翻译吞吐。"
        />
        <MetricCard
          label="令牌用量"
          :value="formatNumber(state?.TotalTokenCount)"
          value-id="totalTokenCount"
          help="服务商返回的累计 token 用量，用于粗略判断成本。"
        />
      </div>

      <SectionPanel
        title="AI 服务"
        description="服务商、模型、密钥和最近一次检测结果。"
      >
        <div class="provider-summary">
          <div>
            <span>服务商</span>
            <strong>{{ state?.ProviderKind ?? "-" }}</strong>
          </div>
          <div>
            <span>模型</span>
            <strong>{{ state?.Model ?? "-" }}</strong>
          </div>
          <div>
            <span>API Key</span>
            <strong>{{ state?.ApiKeyConfigured ? (state.ApiKeyPreview ?? "已配置") : "未配置" }}</strong>
          </div>
          <div>
            <span>检测结果</span>
            <strong :class="`status-${providerTone}`">{{ providerStatusText }}</strong>
          </div>
        </div>
      </SectionPanel>

      <SectionPanel
        title="最近完成"
        description="最近写入指标记录的翻译结果，便于快速确认当前模型输出。"
      >
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
