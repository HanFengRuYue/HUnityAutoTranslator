<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { Copy, Minus, Square, X } from "lucide-vue-next";
import brandIcon from "../../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import { safeInvoke } from "../api/client";

type WindowStateName = "normal" | "maximized";

const isMaximized = ref(false);

function applyState(value: string | null | undefined): void {
  isMaximized.value = value === "maximized";
}

function onStateEvent(event: Event): void {
  const detail = (event as CustomEvent<{ state?: WindowStateName }>).detail;
  applyState(detail?.state);
}

async function minimize(): Promise<void> {
  await safeInvoke("windowMinimize", {}, { silent: true });
}

async function maximizeRestore(): Promise<void> {
  const next = await safeInvoke<string>("windowMaximizeRestore", {}, { silent: true });
  if (typeof next === "string") {
    applyState(next);
  }
}

async function closeWindow(): Promise<void> {
  await safeInvoke("windowClose", {}, { silent: true });
}

onMounted(async () => {
  window.ToolboxBridge?.events?.addEventListener("windowStateChanged", onStateEvent);
  const initial = await safeInvoke<string>("getWindowState", {}, { silent: true });
  if (typeof initial === "string") {
    applyState(initial);
  }
});

onUnmounted(() => {
  window.ToolboxBridge?.events?.removeEventListener("windowStateChanged", onStateEvent);
});
</script>

<template>
  <header class="window-titlebar">
    <div class="window-brand">
      <img :src="brandIcon" alt="" aria-hidden="true" />
      <strong>HUnityAutoTranslator 工具箱</strong>
    </div>
    <div class="window-drag-region" aria-hidden="true"></div>
    <div class="window-controls">
      <button type="button" title="最小化" aria-label="最小化" @click="minimize">
        <Minus class="icon" />
      </button>
      <button type="button" :title="isMaximized ? '向下还原' : '最大化'" :aria-label="isMaximized ? '向下还原' : '最大化'" @click="maximizeRestore">
        <Copy v-if="isMaximized" class="icon" />
        <Square v-else class="icon" />
      </button>
      <button class="close-button" type="button" title="关闭" aria-label="关闭" @click="closeWindow">
        <X class="icon" />
      </button>
    </div>
  </header>
</template>

<style scoped>
.window-titlebar {
  display: flex;
  align-items: center;
  height: 36px;
  flex: 0 0 36px;
  position: relative;
  z-index: 50;
  -webkit-app-region: drag;
  app-region: drag;
  user-select: none;
}

.window-brand {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 0 12px;
  font-size: 12.5px;
  letter-spacing: 0.01em;
}

.window-brand img {
  width: 18px;
  height: 18px;
  border-radius: 5px;
  padding: 1px;
  flex: 0 0 18px;
}

.window-drag-region {
  flex: 1 1 auto;
  height: 100%;
}

.window-controls {
  display: flex;
  align-items: stretch;
  height: 100%;
  -webkit-app-region: no-drag;
  app-region: no-drag;
}

.window-controls button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 46px;
  height: 100%;
  padding: 0;
  border: 0;
  border-radius: 0;
  cursor: pointer;
  -webkit-app-region: no-drag;
  app-region: no-drag;
}

.window-controls button .icon {
  width: 14px;
  height: 14px;
}
</style>
