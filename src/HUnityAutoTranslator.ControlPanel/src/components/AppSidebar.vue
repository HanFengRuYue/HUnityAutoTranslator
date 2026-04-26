<script setup lang="ts">
import { computed } from "vue";
import { controlPanelStore, cycleTheme, setActivePage } from "../state/controlPanelStore";
import type { PageKey } from "../types/api";

const pages: Array<{ key: PageKey; label: string }> = [
  { key: "status", label: "运行状态" },
  { key: "plugin", label: "插件设置" },
  { key: "ai", label: "AI 翻译设置" },
  { key: "glossary", label: "术语库" },
  { key: "editor", label: "文本编辑" },
  { key: "about", label: "版本信息" }
];

const connectionText = computed(() => {
  if (controlPanelStore.connection === "online") {
    return "已连接";
  }

  if (controlPanelStore.connection === "offline") {
    return "连接中断";
  }

  return "正在连接";
});

const themeText = computed(() => {
  if (controlPanelStore.theme === "light") {
    return "浅色";
  }

  if (controlPanelStore.theme === "dark") {
    return "深色";
  }

  return "跟随系统";
});
</script>

<template>
  <aside class="sidebar">
    <div class="brand">
      <strong>HUnityAutoTranslator</strong>
      <span>本机控制面板</span>
    </div>
    <nav class="nav-list" aria-label="功能导航">
      <button
        v-for="page in pages"
        :key="page.key"
        class="nav-item"
        :class="{ active: controlPanelStore.activePage === page.key }"
        type="button"
        @click="setActivePage(page.key)"
      >
        {{ page.label }}
      </button>
    </nav>
    <div class="sidebar-footer">
      <div class="connection" :class="`connection-${controlPanelStore.connection}`">{{ connectionText }}</div>
      <button class="theme-cycle" type="button" @click="cycleTheme">
        <span>主题</span>
        <strong>{{ themeText }}</strong>
      </button>
    </div>
  </aside>
</template>
