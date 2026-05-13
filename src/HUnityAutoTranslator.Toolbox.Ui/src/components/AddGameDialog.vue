<script setup lang="ts">
import { nextTick, ref, watch } from "vue";
import { CheckCircle2, FolderOpen, FolderPlus, X } from "lucide-vue-next";
import { safeInvoke } from "../api/client";
import { buildEntry, inspectRoot } from "../state/gameLibrary";
import {
  closeAddGameDialog,
  showToast,
  toolboxStore,
  upsertGame
} from "../state/toolboxStore";

const manualGameRoot = ref("");
const busy = ref(false);
const message = ref("");
const inputEl = ref<HTMLInputElement | null>(null);

watch(
  () => toolboxStore.addGameDialog.open,
  (open) => {
    if (open) {
      manualGameRoot.value = toolboxStore.addGameDialog.initialPath;
      message.value = "";
      void nextTick(() => inputEl.value?.focus());
    }
  }
);

async function pickPath(): Promise<void> {
  if (busy.value) {
    return;
  }

  const picked = await safeInvoke<string>("pickGameDirectory", {
    initialDirectory: manualGameRoot.value
  });
  if (picked === null) {
    return;
  }
  if (!picked) {
    message.value = "已取消选择目录。";
    return;
  }

  manualGameRoot.value = picked;
  message.value = "";
}

async function submit(): Promise<void> {
  if (busy.value) {
    return;
  }

  const trimmed = manualGameRoot.value.trim();
  if (!trimmed) {
    message.value = "请先填写或选择游戏目录。";
    return;
  }

  busy.value = true;
  try {
    const inspection = await inspectRoot(trimmed);
    if (!inspection) {
      message.value = "检测游戏目录失败，请稍后重试。";
      return;
    }

    const entry = buildEntry(trimmed, inspection);
    upsertGame(entry);

    if (inspection.IsValidUnityGame) {
      showToast(`已添加并选择：${entry.Name}`, "ok");
      closeAddGameDialog();
    } else {
      message.value = `目录已加入游戏库，但没有识别到有效 Unity 游戏：${entry.Root}`;
      showToast(message.value, "warn");
    }
  } catch (error) {
    const errMsg = error instanceof Error ? error.message : "检测游戏目录失败。";
    message.value = errMsg;
    showToast(errMsg, "error");
  } finally {
    busy.value = false;
  }
}

function onEsc(event: KeyboardEvent): void {
  if (busy.value) {
    return;
  }
  event.stopPropagation();
  closeAddGameDialog();
}
</script>

<template>
  <div
    v-if="toolboxStore.addGameDialog.open"
    class="modal-veil"
    role="dialog"
    aria-modal="true"
    @keydown.esc="onEsc"
  >
    <div class="modal-card add-game-card">
      <header class="modal-head">
        <FolderPlus class="modal-icon" />
        <strong>添加游戏</strong>
        <button class="modal-close" type="button" :disabled="busy" @click="closeAddGameDialog">
          <X class="icon" />
        </button>
      </header>
      <div class="modal-body">
        <p class="modal-lead">选择 Unity 游戏根目录，工具箱会自动检测游戏信息。</p>
        <label class="field add-game-field">
          <span>游戏目录</span>
          <div class="add-game-path">
            <input
              ref="inputEl"
              v-model="manualGameRoot"
              spellcheck="false"
              placeholder="例如 D:\Game\YourGame"
              :disabled="busy"
              @keydown.enter.prevent="submit"
            />
            <button type="button" :disabled="busy" @click="pickPath">
              <FolderOpen class="icon" />浏览...
            </button>
          </div>
        </label>
        <p v-if="message" class="add-game-message">{{ message }}</p>
      </div>
      <div class="modal-actions">
        <button type="button" :disabled="busy" @click="closeAddGameDialog">取消</button>
        <button
          class="button-primary"
          type="button"
          :disabled="busy || !manualGameRoot.trim()"
          @click="submit"
        >
          <CheckCircle2 class="icon" />添加并检测
        </button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.add-game-card {
  width: min(540px, calc(100vw - 48px));
}
.modal-lead {
  margin: 0 0 12px;
  color: var(--muted);
  font-size: 13px;
}
.add-game-field {
  display: grid;
  gap: 6px;
}
.add-game-field > span {
  font-size: 12.5px;
  color: var(--muted);
}
.add-game-path {
  display: flex;
  gap: 8px;
}
.add-game-path input {
  flex: 1;
  min-width: 0;
}
.add-game-message {
  margin: 10px 0 0;
  font-size: 12.5px;
  color: var(--muted);
}
</style>
