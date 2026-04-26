<script setup lang="ts">
import { controlPanelStore, refreshState } from "../state/controlPanelStore";
</script>

<template>
  <section class="page active" id="page-status">
    <div class="page-head">
      <div>
        <h1>运行状态</h1>
        <p>当前只接入数据层，详细状态卡片会在下一步迁移。</p>
      </div>
      <button class="secondary" type="button" :disabled="controlPanelStore.isRefreshing" @click="refreshState()">
        {{ controlPanelStore.isRefreshing ? "刷新中" : "刷新" }}
      </button>
    </div>
    <div class="band placeholder-band">
      <p>{{ controlPanelStore.state ? "已读取控制面板状态。" : "正在等待本机控制面板状态。" }}</p>
      <p v-if="controlPanelStore.lastError" class="error-text">{{ controlPanelStore.lastError }}</p>
    </div>
  </section>
</template>
