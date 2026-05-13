<script setup lang="ts">
import { computed } from "vue";
import { Cpu, ExternalLink, Github, Package, ShieldCheck, UserRound } from "lucide-vue-next";
import hunityIconBlueWhite from "../assets/branding/hunity-icon-blue-white-256.png";
import { controlPanelStore } from "../state/controlPanelStore";

const state = computed(() => controlPanelStore.state);
const brandIcon = hunityIconBlueWhite;

const pluginVersion = computed(() => state.value?.PluginVersion?.trim() || "未知");
const bepInExVersion = computed(() => state.value?.BepInExVersion?.trim() || "未检测到");
const projectAuthor = computed(() => state.value?.ProjectAuthor?.trim() || "HanFengRuYue");
const projectRepositoryUrl = computed(() =>
  state.value?.ProjectRepositoryUrl?.trim() || "https://github.com/HanFengRuYue/HUnityAutoTranslator");
const projectRepositoryLabel = computed(() => projectRepositoryUrl.value
  .replace(/^https?:\/\/(?:www\.)?github\.com\//i, "")
  .replace(/^https?:\/\//i, ""));
const llamaCppVersion = computed(() => {
  const status = state.value?.LlamaCppStatus;
  if (!status?.Installed) {
    return "未安装";
  }

  return status.Release?.trim() || "已安装，版本未知";
});
const llamaCppVariant = computed(() => {
  const status = state.value?.LlamaCppStatus;
  return status?.Installed ? status.Variant?.trim() || "" : "";
});
</script>

<template>
  <section class="page active about-page" id="page-about" aria-labelledby="about-title">
    <header class="about-hero">
      <div class="about-hero-logo">
        <img :src="brandIcon" alt="HUnityAutoTranslator" width="96" height="96">
      </div>
      <div class="about-hero-copy">
        <span class="eyebrow">HUnityAutoTranslator</span>
        <h1 id="about-title">版本信息</h1>
        <p>当前插件版本、运行环境和项目来源。</p>
      </div>
      <a
        class="about-hero-repo"
        :href="projectRepositoryUrl"
        target="_blank"
        rel="noreferrer"
        aria-label="GitHub 仓库"
        :title="projectRepositoryUrl"
      >
        <Github class="about-card-icon" aria-hidden="true" />
        <strong>{{ projectRepositoryLabel }}</strong>
        <ExternalLink class="about-link-icon" aria-hidden="true" />
      </a>
    </header>

    <div class="about-card-grid" aria-label="项目版本信息">
      <article class="about-card about-card-primary">
        <Package class="about-card-icon" aria-hidden="true" />
        <span>插件版本</span>
        <strong>{{ pluginVersion }}</strong>
      </article>
      <article class="about-card">
        <Cpu class="about-card-icon" aria-hidden="true" />
        <span>llama.cpp 版本</span>
        <strong>{{ llamaCppVersion }}</strong>
        <small v-if="llamaCppVariant">{{ llamaCppVariant }}</small>
      </article>
      <article class="about-card">
        <ShieldCheck class="about-card-icon" aria-hidden="true" />
        <span>当前 BepInEx 版本</span>
        <strong>{{ bepInExVersion }}</strong>
      </article>
      <article class="about-card">
        <UserRound class="about-card-icon" aria-hidden="true" />
        <span>作者名称</span>
        <strong>{{ projectAuthor }}</strong>
      </article>
    </div>
  </section>
</template>
