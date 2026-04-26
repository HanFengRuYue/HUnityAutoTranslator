<script setup lang="ts">
const url = window.location.href;
</script>

<template>
  <section class="page active" id="page-about">
    <div class="page-head">
      <div>
        <h1>版本信息</h1>
        <p>本机面板、访问范围和构建方式。</p>
      </div>
    </div>

    <div class="about-grid">
      <div class="metric">
        <span>插件</span>
        <strong>HUnityAutoTranslator</strong>
      </div>
      <div class="metric">
        <span>插件版本</span>
        <strong>0.1.0</strong>
      </div>
      <div class="metric">
        <span>控制面板 URL</span>
        <strong>{{ url }}</strong>
      </div>
      <div class="metric">
        <span>访问范围</span>
        <strong>仅本机回环地址</strong>
      </div>
      <div class="metric">
        <span>面板实现</span>
        <strong>Vue / Vite / 内嵌静态 HTML</strong>
      </div>
      <div class="metric">
        <span>缓存与术语</span>
        <strong>本地 SQLite</strong>
      </div>
    </div>
  </section>
</template>
