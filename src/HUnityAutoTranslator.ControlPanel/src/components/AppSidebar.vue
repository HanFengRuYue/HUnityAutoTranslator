<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  Activity,
  Bot,
  BookOpen,
  FileText,
  Info,
  MonitorCog,
  Moon,
  Palette,
  PanelLeftClose,
  PanelLeftOpen,
  Plug,
  Sun
} from "lucide-vue-next";
import { controlPanelStore, cycleTheme, setActivePage } from "../state/controlPanelStore";
import type { PageKey } from "../types/api";

const emit = defineEmits<{
  "update:collapsed": [value: boolean];
}>();

const collapsedStorageKey = "hunity.controlPanel.sidebarCollapsed";
const collapsedControlSize = 40;

const pages: Array<{ key: PageKey; label: string; icon: typeof Activity }> = [
  { key: "status", label: "运行状态", icon: Activity },
  { key: "plugin", label: "插件设置", icon: Plug },
  { key: "ai", label: "AI 翻译设置", icon: Bot },
  { key: "glossary", label: "术语库", icon: BookOpen },
  { key: "editor", label: "文本编辑", icon: FileText },
  { key: "about", label: "版本信息", icon: Info }
];

const collapsed = ref(localStorage.getItem(collapsedStorageKey) === "true");

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

const themeIcon = computed(() => {
  if (controlPanelStore.theme === "light") {
    return Sun;
  }

  if (controlPanelStore.theme === "dark") {
    return Moon;
  }

  return MonitorCog;
});

function toggleCollapsed(): void {
  collapsed.value = !collapsed.value;
}

watch(collapsed, (value) => {
  localStorage.setItem(collapsedStorageKey, String(value));
  emit("update:collapsed", value);
}, { immediate: true });
</script>

<template>
  <aside class="sidebar" :class="{ collapsed }" :style="`--collapsed-control-size: ${collapsedControlSize}px`">
    <div class="brand">
      <strong>HUnity</strong>
      <span v-if="!collapsed">控制面板</span>
    </div>
    <button
      class="sidebar-collapse"
      type="button"
      :title="collapsed ? '展开侧边栏' : '收起侧边栏'"
      @click="toggleCollapsed"
    >
      <PanelLeftOpen v-if="collapsed" class="nav-icon" />
      <PanelLeftClose v-else class="nav-icon" />
      <span v-if="!collapsed">收起</span>
    </button>
    <nav class="nav-list" aria-label="功能导航">
      <button
        v-for="page in pages"
        :key="page.key"
        class="nav-item"
        :class="{ active: controlPanelStore.activePage === page.key }"
        type="button"
        :title="page.label"
        @click="setActivePage(page.key)"
      >
        <component :is="page.icon" class="nav-icon" />
        <span class="nav-copy">
          <strong>{{ page.label }}</strong>
        </span>
      </button>
    </nav>
    <div class="sidebar-footer">
      <div class="connection" :class="`connection-${controlPanelStore.connection}`" :title="connectionText">
        <span v-if="!collapsed">{{ connectionText }}</span>
      </div>
      <button class="theme-cycle" type="button" :title="`主题：${themeText}`" @click="cycleTheme">
        <component v-if="collapsed" :is="themeIcon" class="nav-icon" />
        <Palette v-else class="nav-icon" />
        <span v-if="!collapsed">主题</span>
        <strong v-if="!collapsed">{{ themeText }}</strong>
      </button>
    </div>
  </aside>
</template>
