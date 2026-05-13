<script setup lang="ts">
import type { Component } from "vue";

withDefaults(defineProps<{
  label: string;
  value: string | number;
  help: string;
  valueId?: string;
  icon?: Component;
  tone?: "neutral" | "ok" | "warn" | "danger";
}>(), {
  tone: "neutral"
});
</script>

<template>
  <article class="metric" :class="`metric-${tone}`" tabindex="0" :data-help="help">
    <div class="metric-head">
      <component v-if="icon" :is="icon" class="metric-icon" aria-hidden="true" />
      <span class="metric-label">{{ label }}</span>
    </div>
    <strong :id="valueId">{{ value }}</strong>
  </article>
</template>
