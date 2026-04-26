<script setup lang="ts">
withDefaults(defineProps<{
  label: string;
  value: string | number;
  help: string;
  valueId?: string;
  tone?: "neutral" | "ok" | "warn" | "danger";
}>(), {
  tone: "neutral"
});
</script>

<template>
  <article class="metric" :class="`metric-${tone}`" tabindex="0" :data-help="help">
    <span>{{ label }}</span>
    <strong :id="valueId">{{ value }}</strong>
  </article>
</template>
