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
const projectRepositoryParts = computed(() => {
  const [owner, ...nameParts] = projectRepositoryLabel.value.split("/");
  return {
    owner: owner || "HanFengRuYue",
    name: nameParts.join("/") || "HUnityAutoTranslator"
  };
});
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
  <section class="page active about-page" id="page-about">
    <div class="about-stage" aria-labelledby="about-title">
      <img class="about-watermark" :src="brandIcon" alt="" aria-hidden="true">

      <div class="about-brand-panel">
        <div class="about-logo-mark">
          <img :src="brandIcon" alt="HUnityAutoTranslator" width="128" height="128">
        </div>
        <div class="about-title-block">
          <span class="eyebrow">HUnityAutoTranslator</span>
          <h1 id="about-title">版本信息</h1>
          <p>当前插件版本、运行环境和项目来源。</p>
        </div>
        <div class="about-primary-version">
          <Package class="about-card-icon" aria-hidden="true" />
          <span>插件版本</span>
          <strong>{{ pluginVersion }}</strong>
        </div>
      </div>

      <div class="about-detail-panel">
        <div class="about-detail-head">
          <span>Runtime Plate</span>
          <strong>环境铭牌</strong>
        </div>

        <dl class="about-version-list" aria-label="项目版本信息">
          <div class="about-version-row">
            <dt><Cpu class="about-card-icon" aria-hidden="true" />llama.cpp 版本</dt>
            <dd>
              <strong>{{ llamaCppVersion }}</strong>
              <small v-if="llamaCppVariant">{{ llamaCppVariant }}</small>
            </dd>
          </div>

          <div class="about-version-row">
            <dt><ShieldCheck class="about-card-icon" aria-hidden="true" />当前 BepInEx 版本</dt>
            <dd><strong>{{ bepInExVersion }}</strong></dd>
          </div>

          <div class="about-version-row">
            <dt><UserRound class="about-card-icon" aria-hidden="true" />作者名称</dt>
            <dd><strong>{{ projectAuthor }}</strong></dd>
          </div>

          <div class="about-version-row about-repository-row">
            <dt><Github class="about-card-icon" aria-hidden="true" />GitHub 仓库</dt>
            <dd>
              <a :href="projectRepositoryUrl" target="_blank" rel="noreferrer">
                <strong class="about-repository-path">
                  <span class="about-repo-owner">{{ projectRepositoryParts.owner }}/</span>
                  <span class="about-repo-name">{{ projectRepositoryParts.name }}</span>
                </strong>
                <ExternalLink class="about-link-icon" aria-hidden="true" />
              </a>
            </dd>
          </div>
        </dl>
      </div>
    </div>
  </section>
</template>
