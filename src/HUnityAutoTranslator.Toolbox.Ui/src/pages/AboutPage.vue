<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  Database,
  Gamepad2,
  Languages,
  MonitorCog,
  PackageCheck
} from "lucide-vue-next";
import brandIcon from "../../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import { safeInvoke } from "../api/client";
import { selectedGame as selectedGameFromStore, toolboxStore } from "../state/toolboxStore";
import type { AppInfo } from "../types/api";

const selectedGame = computed(() => selectedGameFromStore());
const selectedInspection = computed(() => selectedGame.value?.Inspection ?? null);
const runtimeSummary = computed(() => selectedInspection.value
  ? `${selectedInspection.value.Backend} ${selectedInspection.value.Architecture}`
  : "未检测");

const appVersion = ref("0.1.1");

onMounted(async () => {
  const info = await safeInvoke<AppInfo>("getAppInfo", {}, { silent: true });
  if (info?.Version) {
    appVersion.value = info.Version;
  }
});
</script>

<template>
  <section class="page about-page">
    <header class="page-hero">
      <div>
        <span class="eyebrow">HUnityAutoTranslator</span>
        <h1>关于工具箱</h1>
        <p>面向本机游戏目录的安装、配置和离线译文维护工具。</p>
      </div>
      <div class="status-strip">
        <span class="pill">v{{ appVersion }}</span>
      </div>
    </header>

    <div class="about-stage">
      <img class="about-watermark" :src="brandIcon" alt="" aria-hidden="true">
      <div class="about-brand-panel">
        <div class="about-logo-mark">
          <img :src="brandIcon" alt="HUnityAutoTranslator" width="128" height="128">
        </div>
        <div class="about-title-block">
          <span class="eyebrow">HUnityAutoTranslator</span>
          <h1>外部工具箱</h1>
          <p>面向本机游戏目录的安装、配置和离线译文维护工具。</p>
        </div>
        <div class="about-primary-version">
          <PackageCheck class="about-card-icon" />
          <span>工具箱版本</span>
          <strong>{{ appVersion }}</strong>
        </div>
      </div>
      <div class="about-detail-panel">
        <div class="about-detail-head">
          <span>Runtime Plate</span>
          <strong>环境铭牌</strong>
        </div>
        <dl class="about-version-list">
          <div class="about-version-row">
            <dt><Gamepad2 class="about-card-icon" />当前游戏</dt>
            <dd><strong>{{ selectedGame?.Name ?? "未选择" }}</strong><small>{{ selectedGame?.Root ?? "游戏库为空" }}</small></dd>
          </div>
          <div class="about-version-row">
            <dt><MonitorCog class="about-card-icon" />运行时</dt>
            <dd><strong>{{ runtimeSummary }}</strong></dd>
          </div>
          <div class="about-version-row">
            <dt><Languages class="about-card-icon" />配置文件</dt>
            <dd><strong>{{ toolboxStore.pluginSettingsPath || "选择游戏后显示" }}</strong></dd>
          </div>
          <div class="about-version-row">
            <dt><Database class="about-card-icon" />译文缓存</dt>
            <dd><strong>{{ selectedGame ? `${selectedGame.Root}\\BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite` : "选择游戏后显示" }}</strong></dd>
          </div>
        </dl>
      </div>
    </div>
  </section>
</template>
