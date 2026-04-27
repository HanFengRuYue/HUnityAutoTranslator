<script setup lang="ts">
import { computed } from "vue";
import AppSidebar from "./components/AppSidebar.vue";
import ToastHost from "./components/ToastHost.vue";
import { controlPanelStore } from "./state/controlPanelStore";
import AboutPage from "./pages/AboutPage.vue";
import AiSettingsPage from "./pages/AiSettingsPage.vue";
import GlossaryPage from "./pages/GlossaryPage.vue";
import PluginSettingsPage from "./pages/PluginSettingsPage.vue";
import StatusPage from "./pages/StatusPage.vue";
import TextEditorPage from "./pages/TextEditorPage.vue";

const pages = {
  status: StatusPage,
  plugin: PluginSettingsPage,
  ai: AiSettingsPage,
  glossary: GlossaryPage,
  editor: TextEditorPage,
  about: AboutPage
};

const pageLabels = {
  status: "运行状态",
  plugin: "插件设置",
  ai: "AI 翻译设置",
  glossary: "术语库",
  editor: "文本编辑",
  about: "版本信息"
};

const providerNames: Record<string, string> = {
  "0": "OpenAI",
  "1": "DeepSeek",
  "2": "兼容接口"
};

const connectionText = computed(() => {
  if (controlPanelStore.connection === "online") {
    return "已连接";
  }

  if (controlPanelStore.connection === "offline") {
    return "连接中断";
  }

  return "连接中";
});

const queueText = computed(() => {
  const state = controlPanelStore.state;
  return String(state ? state.QueueCount ?? state.QueuedTextCount ?? 0 : 0);
});

const providerText = computed(() => {
  const state = controlPanelStore.state;
  if (!state) {
    return "-";
  }

  const provider = providerNames[String(state.ProviderKind)] ?? String(state.ProviderKind ?? "-");
  return `${provider} / ${state.Model || "-"}`;
});

const lastRefreshText = computed(() => {
  if (!controlPanelStore.lastRefreshUtc) {
    return "尚未同步";
  }

  const date = new Date(controlPanelStore.lastRefreshUtc);
  if (Number.isNaN(date.getTime())) {
    return controlPanelStore.lastRefreshUtc;
  }

  return new Intl.DateTimeFormat("zh-CN", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  }).format(date);
});

const activePageComponent = computed(() => pages[controlPanelStore.activePage]);
const activePageLabel = computed(() => pageLabels[controlPanelStore.activePage]);
</script>

<template>
  <div class="app-shell">
    <AppSidebar />
    <main class="workspace">
      <header class="workspace-topbar">
        <div class="workspace-title">
          <span>本机控制台 / {{ activePageLabel }}</span>
          <strong>HUnityAutoTranslator</strong>
        </div>
        <div class="runtime-strip" aria-label="运行摘要">
          <span class="runtime-pill" :class="`runtime-${controlPanelStore.connection}`">{{ connectionText }}</span>
          <span class="runtime-stat"><span>队列</span><strong>{{ queueText }}</strong></span>
          <span class="runtime-stat"><span>服务</span><strong>{{ providerText }}</strong></span>
          <span class="runtime-stat"><span>同步</span><strong>{{ lastRefreshText }}</strong></span>
        </div>
      </header>
      <div class="workspace-main">
        <component :is="activePageComponent" />
      </div>
    </main>
    <ToastHost />
  </div>
</template>
