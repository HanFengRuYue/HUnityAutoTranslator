<script setup lang="ts">
import { controlPanelStore, dismissToast } from "../state/controlPanelStore";
</script>

<template>
  <div class="toast-host" aria-live="polite" aria-atomic="false">
    <button
      v-for="toast in controlPanelStore.toasts"
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
