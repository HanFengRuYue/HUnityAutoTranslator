<script setup lang="ts">
import { computed } from "vue";
import { AlertTriangle, Save, Trash2, X } from "lucide-vue-next";
import {
  cancelDirtyLeave,
  discardDirtyLeave,
  saveThenLeaveDirty,
  toolboxStore
} from "../state/toolboxStore";

const formLabels: Record<string, string> = {
  pluginConfig: "插件配置"
};

const dirtyFormNames = computed(() =>
  Array.from(toolboxStore.dirtyForms).map((key) => formLabels[key] ?? key)
);
</script>

<template>
  <div v-if="toolboxStore.dirtyDialog.open" class="modal-veil" role="dialog" aria-modal="true">
    <div class="modal-card">
      <header class="modal-head">
        <AlertTriangle class="modal-icon" />
        <strong>未保存的修改</strong>
        <button class="modal-close" type="button" @click="cancelDirtyLeave"><X class="icon" /></button>
      </header>
      <div class="modal-body">
        <p>以下表单存在未保存的改动：</p>
        <ul>
          <li v-for="name in dirtyFormNames" :key="name">{{ name }}</li>
        </ul>
        <p>切换页面会丢失这些改动。</p>
      </div>
      <div class="modal-actions">
        <button type="button" @click="cancelDirtyLeave">继续修改</button>
        <button class="button-danger" type="button" @click="discardDirtyLeave"><Trash2 class="icon" />放弃修改</button>
        <button class="button-primary" type="button" @click="saveThenLeaveDirty"><Save class="icon" />保存并离开</button>
      </div>
    </div>
  </div>
</template>
