<script setup lang="ts">
const props = defineProps<{
  title: string;
  description?: string;
}>();

const headingId = `section-${props.title.replace(/\s+/g, "-").toLowerCase()}`;
</script>

<template>
  <section class="section-panel" :aria-labelledby="headingId">
    <div class="section-head">
      <div>
        <h2 :id="headingId">{{ title }}</h2>
        <p v-if="description">{{ description }}</p>
      </div>
      <slot name="actions" />
    </div>
    <slot />
  </section>
</template>
