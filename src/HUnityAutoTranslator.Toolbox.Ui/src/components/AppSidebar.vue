<script setup lang="ts">
import { computed } from "vue";
import {
  Database,
  Download,
  Gamepad2,
  Info,
  MonitorCog,
  Moon,
  Palette,
  PanelLeftClose,
  PanelLeftOpen,
  Settings,
  Sun
} from "lucide-vue-next";
import type { Component } from "vue";
import brandIcon from "../../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import {
  cycleTheme,
  requestNavigation,
  selectedInspection,
  setSidebarCollapsed,
  toolboxStore
} from "../state/toolboxStore";
import type { PageKey } from "../types/api";

const pages: Array<{ key: PageKey; label: string; icon: Component }> = [
  { key: "library", label: "游戏库", icon: Gamepad2 },
  { key: "install", label: "自动安装", icon: Download },
  { key: "config", label: "插件配置", icon: Settings },
  { key: "translations", label: "译文编辑", icon: Database },
  { key: "about", label: "关于工具箱", icon: Info }
];

const collapsedControlSize = 40;

const themeText = computed(() => {
  if (toolboxStore.theme === "light") {
    return "浅色";
  }

  if (toolboxStore.theme === "dark") {
    return "深色";
  }

  return "跟随系统";
});

const themeIcon = computed(() => {
  if (toolboxStore.theme === "light") {
    return Sun;
  }

  if (toolboxStore.theme === "dark") {
    return Moon;
  }

  return MonitorCog;
});

const detectionStatus = computed<{ kind: "ok" | "warn" | "danger" | "idle"; label: string }>(() => {
  if (toolboxStore.isInspecting) {
    return { kind: "warn", label: "检测中..." };
  }

  if (toolboxStore.isPlanningInstall) {
    return { kind: "warn", label: "规划安装中..." };
  }

  if (toolboxStore.isSavingPluginConfig) {
    return { kind: "warn", label: "保存配置中..." };
  }

  if (toolboxStore.isLoadingPluginConfig) {
    return { kind: "warn", label: "读取配置中..." };
  }

  const inspection = selectedInspection();
  if (!inspection) {
    return { kind: "idle", label: "未检测" };
  }

  if (!inspection.IsValidUnityGame) {
    return { kind: "danger", label: "非 Unity 游戏" };
  }

  if (!inspection.BepInExInstalled) {
    return { kind: "warn", label: "BepInEx 缺失" };
  }

  if (!inspection.PluginInstalled) {
    return { kind: "warn", label: "插件未安装" };
  }

  return { kind: "ok", label: `${inspection.Backend} · 已安装` };
});

function toggleCollapsed(): void {
  setSidebarCollapsed(!toolboxStore.sidebarCollapsed);
}
</script>

<template>
  <aside class="sidebar" :class="{ collapsed: toolboxStore.sidebarCollapsed }" :style="`--collapsed-control-size: ${collapsedControlSize}px`">
    <div class="brand">
      <img class="brand-logo" :src="brandIcon" alt="HUnity" width="36" height="36">
      <div v-if="!toolboxStore.sidebarCollapsed" class="brand-copy">
        <strong>HUnity</strong>
        <span>外部工具箱</span>
      </div>
    </div>
    <button
      class="sidebar-collapse"
      type="button"
      :title="toolboxStore.sidebarCollapsed ? '展开侧边栏' : '收起侧边栏'"
      @click="toggleCollapsed"
    >
      <PanelLeftOpen v-if="toolboxStore.sidebarCollapsed" class="nav-icon" />
      <PanelLeftClose v-else class="nav-icon" />
      <span v-if="!toolboxStore.sidebarCollapsed">收起</span>
    </button>
    <nav class="nav-list" aria-label="功能导航">
      <button
        v-for="page in pages"
        :key="page.key"
        class="nav-item"
        :class="{ active: toolboxStore.activePage === page.key }"
        type="button"
        :title="page.label"
        @click="requestNavigation(page.key)"
      >
        <component :is="page.icon" class="nav-icon" />
        <span class="nav-copy">
          <strong>{{ page.label }}</strong>
        </span>
      </button>
    </nav>
    <div class="sidebar-footer">
      <div class="connection" :class="`connection-${detectionStatus.kind}`" :title="detectionStatus.label">
        <span v-if="!toolboxStore.sidebarCollapsed">{{ detectionStatus.label }}</span>
      </div>
      <button class="theme-cycle" type="button" :title="`主题：${themeText}`" @click="cycleTheme">
        <component v-if="toolboxStore.sidebarCollapsed" :is="themeIcon" class="nav-icon" />
        <Palette v-else class="nav-icon" />
        <span v-if="!toolboxStore.sidebarCollapsed">主题</span>
        <strong v-if="!toolboxStore.sidebarCollapsed">{{ themeText }}</strong>
      </button>
    </div>
  </aside>
</template>
