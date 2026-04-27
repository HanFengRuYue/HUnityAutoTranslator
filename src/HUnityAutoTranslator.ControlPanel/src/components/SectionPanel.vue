<script setup lang="ts">
import type { Component } from "vue";

const props = defineProps<{
  title: string;
  description?: string;
  icon?: Component;
}>();

const headingId = `section-${props.title.replace(/\s+/g, "-").toLowerCase()}`;
</script>

<template>
  <section class="section-panel" :aria-labelledby="headingId">
    <div class="section-head">
      <div>
        <div class="section-title-row">
          <component v-if="icon" :is="icon" class="section-icon" aria-hidden="true" />
          <h2 :id="headingId">{{ title }}</h2>
        </div>
        <p v-if="description">{{ description }}</p>
      </div>
      <div v-if="$slots.actions" class="section-actions">
        <slot name="actions" />
      </div>
    </div>
    <slot />
  </section>
</template>
