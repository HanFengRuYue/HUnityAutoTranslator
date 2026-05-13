<script setup lang="ts">
import { computed, ref } from "vue";
import {
  CheckCircle2,
  FolderPlus,
  Gamepad2,
  Grid2X2,
  List,
  RefreshCw,
  Trash2
} from "lucide-vue-next";
import brandIcon from "../../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import SectionPanel from "../components/SectionPanel.vue";
import { buildEntry, inspectRoot } from "../state/gameLibrary";
import {
  openAddGameDialog,
  persistGames,
  removeGame as removeGameFromStore,
  setLibraryAccent,
  setLibraryLayout,
  setLibraryPosterSize,
  setSelectedGameId,
  showToast,
  toolboxStore,
  upsertGame
} from "../state/toolboxStore";
import type { LibraryAccent } from "../types/api";

const libraryAccentOptions: LibraryAccent[] = ["blue", "green", "amber", "rose"];

const busy = ref(false);

const selectedGame = computed(() => toolboxStore.games.find((game) => game.Id === toolboxStore.selectedGameId) ?? null);
const selectedInspection = computed(() => selectedGame.value?.Inspection ?? null);
const selectedGameRoot = computed(() => selectedGame.value?.Root ?? "");
const runtimeSummary = computed(() => selectedInspection.value
  ? `${selectedInspection.value.Backend} ${selectedInspection.value.Architecture}`
  : "未检测");
const selectedGameIsReady = computed(() => Boolean(selectedInspection.value?.IsValidUnityGame));

function selectGame(id: string): void {
  setSelectedGameId(id);
}

async function refreshSelectedGame(): Promise<void> {
  if (!selectedGame.value || busy.value) {
    return;
  }

  busy.value = true;
  try {
    const inspection = await inspectRoot(selectedGame.value.Root);
    if (!inspection) {
      showToast("检测游戏目录失败，请稍后重试。", "error");
      return;
    }

    const entry = buildEntry(selectedGame.value.Root, inspection);
    upsertGame(entry);
    const summary = inspection.IsValidUnityGame
      ? `检测完成：${entry.Name}，${inspection.Backend} ${inspection.Architecture}`
      : "没有识别到有效 Unity 游戏目录。";
    showToast(summary, inspection.IsValidUnityGame ? "ok" : "warn");
  } finally {
    busy.value = false;
  }
}

function removeGame(id: string): void {
  removeGameFromStore(id);
  persistGames();
  showToast("已从游戏库移除。", "ok");
}
</script>

<template>
  <section class="page library-page" :class="[`accent-${toolboxStore.libraryAccent}`, `library-${toolboxStore.libraryLayout}`, `poster-${toolboxStore.libraryPosterSize}`]">
    <header class="page-hero">
      <div>
        <span class="eyebrow">{{ selectedGame ? selectedGame.Name : "未选择游戏" }}</span>
        <h1>游戏库</h1>
        <p>{{ selectedGame ? selectedGame.Root : "游戏库不会默认选择你电脑里的任何目录。" }}</p>
      </div>
      <div class="status-strip">
        <span class="pill">{{ runtimeSummary }}</span>
        <span class="pill">{{ selectedInspection?.BepInExVersion ?? "BepInEx 未检测" }}</span>
        <span class="pill">{{ selectedInspection?.PluginInstalled ? "插件已存在" : "插件未安装" }}</span>
      </div>
    </header>

    <SectionPanel title="当前游戏" description="安装、配置、译文编辑都会使用这里选中的游戏。">
      <template #actions>
        <button class="button-primary" type="button" :disabled="busy" @click="openAddGameDialog(selectedGameRoot)">
          <FolderPlus class="icon" />添加游戏目录
        </button>
        <button type="button" :disabled="!selectedGame || busy" @click="refreshSelectedGame">
          <RefreshCw class="icon" />检测当前
        </button>
      </template>
      <div class="library-hero">
        <div class="library-cover">
          <img :src="brandIcon" alt="" aria-hidden="true">
        </div>
        <div class="library-hero-copy">
          <span class="eyebrow">当前游戏</span>
          <h2>{{ selectedGame?.Name ?? "尚未选择" }}</h2>
          <p>{{ selectedGame?.Root ?? "添加目录后，安装、配置和译文编辑都会使用这里选中的游戏。" }}</p>
          <div class="hero-badges">
            <span class="badge">{{ selectedGameIsReady ? "Unity 游戏" : "未检测" }}</span>
            <span class="badge">{{ runtimeSummary }}</span>
            <span class="badge">{{ toolboxStore.games.length }} 个目录</span>
          </div>
        </div>
      </div>
    </SectionPanel>

    <SectionPanel title="已添加的游戏" description="点击卡片切换当前选中的游戏。">
      <template #actions>
        <div class="segmented">
          <button type="button" :class="{ active: toolboxStore.libraryLayout === 'grid' }" title="封面网格" @click="setLibraryLayout('grid')">
            <Grid2X2 class="icon" />
          </button>
          <button type="button" :class="{ active: toolboxStore.libraryLayout === 'list' }" title="列表" @click="setLibraryLayout('list')">
            <List class="icon" />
          </button>
        </div>
        <select :value="toolboxStore.libraryPosterSize" title="库项目尺寸" @change="setLibraryPosterSize(($event.target as HTMLSelectElement).value as 'compact' | 'normal' | 'large')">
          <option value="compact">紧凑</option>
          <option value="normal">标准</option>
          <option value="large">大封面</option>
        </select>
        <div class="swatches" aria-label="游戏库强调色">
          <button v-for="accent in libraryAccentOptions" :key="accent" type="button" :class="['swatch', accent, { active: toolboxStore.libraryAccent === accent }]" @click="setLibraryAccent(accent)"></button>
        </div>
      </template>
      <div v-if="toolboxStore.games.length" class="game-library">
        <article
          v-for="game in toolboxStore.games"
          :key="game.Id"
          class="game-tile"
          :class="{ selected: toolboxStore.selectedGameId === game.Id, invalid: game.Inspection && !game.Inspection.IsValidUnityGame }"
          @click="selectGame(game.Id)"
        >
          <div class="game-poster">
            <img :src="brandIcon" alt="" aria-hidden="true">
          </div>
          <div class="game-info">
            <strong>{{ game.Name }}</strong>
            <span>{{ game.Root }}</span>
            <div class="tile-badges">
              <span>{{ game.Inspection?.Backend ?? "未检测" }}</span>
              <span>{{ game.Inspection?.BepInExVersion ?? "BepInEx 未检测" }}</span>
              <span>{{ game.Inspection?.PluginInstalled ? "插件已安装" : "插件未安装" }}</span>
            </div>
          </div>
          <div class="game-actions" @click.stop>
            <button type="button" title="选择" @click="selectGame(game.Id)"><CheckCircle2 class="icon" /></button>
            <button class="button-danger" type="button" title="从游戏库移除" @click="removeGame(game.Id)"><Trash2 class="icon" /></button>
          </div>
        </article>
      </div>
      <div v-else class="empty-state">
        <Gamepad2 class="empty-icon" />
        <strong>游戏库为空</strong>
        <span>点击右上角「添加游戏目录」开始。</span>
      </div>
    </SectionPanel>
  </section>
</template>
