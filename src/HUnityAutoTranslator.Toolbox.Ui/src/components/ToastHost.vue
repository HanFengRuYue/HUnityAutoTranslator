<script setup lang="ts">
import { dismissToast, toolboxStore } from "../state/toolboxStore";
</script>

<template>
  <div class="toast-host" aria-live="polite" aria-atomic="false">
    <button
      v-for="toast in toolboxStore.toasts"
      :key="toast.id"
      class="toast"
      :class="`toast-${toast.kind}`"
      type="button"
      @click="dismissToast(toast.id)"
    >
      {{ toast.message }}
    </button>
  </div>
</template>
