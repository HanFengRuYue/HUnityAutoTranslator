<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import {
  ChevronLeft,
  ChevronRight,
  Download,
  Grid2X2,
  Images,
  Layers3,
  List,
  RefreshCw,
  ScanLine,
  Trash2,
  Upload
} from "lucide-vue-next";
import { api, buildQuery, getJson } from "../api/client";
import MetricCard from "../components/MetricCard.vue";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, showToast } from "../state/controlPanelStore";
import type {
  TextureCatalogItem,
  TextureCatalogPage,
  TextureImportResult,
  TextureOverrideClearResult,
  TextureScanResult
} from "../types/api";
import { formatDateTime, formatNumber } from "../utils/format";

type TextureViewMode = "list" | "gallery";

const catalog = ref<TextureCatalogPage | null>(null);
const catalogLoading = ref(false);
const scanStarting = ref(false);
const importing = ref(false);
const exporting = ref(false);
const importFile = ref<HTMLInputElement | null>(null);
const operationErrors = ref<string[]>([]);
const selectedScene = ref("");
const viewMode = ref<TextureViewMode>("list");
const currentPage = ref(1);
let scanPollTimer: number | null = null;

const items = computed(() => catalog.value?.Items ?? []);
const errors = computed(() => catalog.value?.Errors ?? []);
const scenes = computed(() => catalog.value?.Scenes ?? []);
const scanStatus = computed(() => catalog.value?.ScanStatus ?? null);
const isScanning = computed(() => scanStarting.value || scanStatus.value?.IsScanning === true);
const pageSize = computed(() => viewMode.value === "gallery" ? 48 : 20);
const filteredCount = computed(() => catalog.value?.FilteredCount ?? 0);
const totalPages = computed(() => Math.max(1, Math.ceil(filteredCount.value / pageSize.value)));
const scannedText = computed(() => catalog.value?.ScannedUtc ? formatDateTime(catalog.value.ScannedUtc) : "尚未扫描");
const scanStatusText = computed(() => {
  if (!scanStatus.value) {
    return "等待扫描";
  }

  if (scanStatus.value.IsScanning) {
    return `${scanStatus.value.Message} 已处理 ${formatNumber(scanStatus.value.ProcessedTargets)} 个目标`;
  }

  return "空闲";
});

function clearScanPoll(): void {
  if (scanPollTimer !== null) {
    window.clearTimeout(scanPollTimer);
    scanPollTimer = null;
  }
}

function scheduleScanPoll(): void {
  clearScanPoll();
  if (catalog.value?.ScanStatus?.IsScanning) {
    scanPollTimer = window.setTimeout(() => {
      void loadCatalog();
    }, 900);
  }
}

async function loadCatalog(): Promise<void> {
  catalogLoading.value = true;
  try {
    catalog.value = await getJson<TextureCatalogPage>(buildQuery("/api/textures", {
      scene: selectedScene.value,
      offset: (currentPage.value - 1) * pageSize.value,
      limit: pageSize.value
    }));
    if (currentPage.value > totalPages.value) {
      currentPage.value = totalPages.value;
      return;
    }

    scheduleScanPoll();
  } catch (error) {
    showToast(error instanceof Error ? error.message : "贴图目录加载失败。", "error");
  } finally {
    catalogLoading.value = false;
  }
}

async function scanTextures(): Promise<void> {
  scanStarting.value = true;
  operationErrors.value = [];
  try {
    const result = await api<TextureScanResult>("/api/textures/scan", { method: "POST" });
    await loadCatalog();
    showToast(result.Message || "贴图扫描已开始。", result.IsScanning ? "info" : result.Errors.length ? "warn" : "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "贴图扫描失败。", "error");
  } finally {
    scanStarting.value = false;
  }
}

function safeFileNamePart(value: string | null | undefined): string {
  const cleaned = (value ?? "")
    .trim()
    .replace(/[<>:"\/\\|?*\x00-\x1f]+/g, "-")
    .replace(/\s+/g, " ")
    .replace(/\.+$/g, "")
    .trim();
  return cleaned || "unknown-game";
}

function fallbackExportName(): string {
  const gameName = safeFileNamePart(controlPanelStore.state?.GameTitle || controlPanelStore.state?.AutomaticGameTitle);
  const stamp = new Date().toISOString().replace(/[-:T]/g, "").slice(0, 15);
  return `hunity-textures-${gameName}-${stamp}.zip`;
}

function fileNameFromDisposition(disposition: string | null): string {
  const match = /filename="?([^";]+)"?/i.exec(disposition ?? "");
  return match?.[1] || fallbackExportName();
}

async function exportTextures(): Promise<void> {
  exporting.value = true;
  operationErrors.value = [];
  try {
    const response = await fetch(buildQuery("/api/textures/export", { scene: selectedScene.value }), { cache: "no-store" });
    if (!response.ok) {
      throw new Error(await response.text() || `导出失败：HTTP ${response.status}`);
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileNameFromDisposition(response.headers.get("content-disposition"));
    anchor.click();
    URL.revokeObjectURL(url);
    await loadCatalog();
    showToast("贴图包导出已开始。", "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "贴图包导出失败。", "error");
  } finally {
    exporting.value = false;
  }
}

function openImportPicker(): void {
  if (importFile.value) {
    importFile.value.value = "";
  }

  importFile.value?.click();
}

async function importTextures(event: Event): Promise<void> {
  const file = (event.target as HTMLInputElement).files?.[0];
  if (!file) {
    return;
  }

  importing.value = true;
  operationErrors.value = [];
  try {
    const result = await api<TextureImportResult>("/api/textures/import", {
      method: "POST",
      body: file
    });
    await loadCatalog();
    operationErrors.value = [...result.Errors];
    showToast(`已导入 ${result.ImportedCount} 张贴图，已应用 ${result.AppliedCount} 个引用。`, result.Errors.length ? "warn" : "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "贴图包导入失败。", "error");
  } finally {
    importing.value = false;
    if (importFile.value) {
      importFile.value.value = "";
    }
  }
}

async function clearTextureOverrides(): Promise<void> {
  if (!confirm("清空已导入的贴图覆盖，并恢复当前已知目标？")) {
    return;
  }

  scanStarting.value = true;
  operationErrors.value = [];
  try {
    const result = await api<TextureOverrideClearResult>("/api/textures/overrides", { method: "DELETE" });
    await loadCatalog();
    operationErrors.value = [...result.Errors];
    showToast(`已清空 ${result.DeletedCount} 个覆盖文件，已恢复 ${result.RestoredCount} 个引用。`, result.Errors.length ? "warn" : "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "清空贴图覆盖失败。", "error");
  } finally {
    scanStarting.value = false;
  }
}

function primaryReference(item: TextureCatalogItem): string {
  const reference = item.References[0];
  if (!reference) {
    return "无引用";
  }

  return [reference.SceneName, reference.ComponentHierarchy, reference.ComponentType]
    .filter((part) => part && part.length > 0)
    .join(" / ") || reference.TargetId;
}

function textureImageUrl(item: TextureCatalogItem): string {
  return `/api/textures/${encodeURIComponent(item.SourceHash)}/image`;
}

function textureMeta(item: TextureCatalogItem): string {
  return `${item.Width} x ${item.Height} · ${item.Format} · ${item.ReferenceCount} 个引用`;
}

function setViewMode(mode: TextureViewMode): void {
  viewMode.value = mode;
}

function goToPage(page: number): void {
  currentPage.value = Math.min(totalPages.value, Math.max(1, page));
}

watch([selectedScene, viewMode], () => {
  currentPage.value = 1;
  void loadCatalog();
});

watch(currentPage, () => {
  void loadCatalog();
});

onMounted(() => {
  void loadCatalog();
});

onUnmounted(clearScanPoll);
</script>

<template>
  <section class="page active texture-page" id="page-textures">
    <div class="page-head">
      <div>
        <h1>贴图替换</h1>
        <p>扫描并导出已发现贴图，把修改后的 PNG 持久化替换回游戏。</p>
      </div>
      <div class="actions">
        <button id="scanTextures" class="secondary" type="button" :disabled="isScanning" @click="scanTextures">
          <ScanLine class="button-icon" />
          {{ isScanning ? "扫描中..." : "扫描贴图" }}
        </button>
        <button id="exportTextures" class="primary" type="button" :disabled="exporting" @click="exportTextures">
          <Download class="button-icon" />
          {{ exporting ? "导出中..." : "导出贴图包" }}
        </button>
      </div>
    </div>

    <div class="texture-summary">
      <MetricCard label="贴图" :value="formatNumber(catalog?.TextureCount)" help="已记录到贴图目录的去重 PNG 数量。" />
      <MetricCard label="引用" :value="formatNumber(catalog?.ReferenceCount)" help="已记录到贴图目录的组件引用数量。" />
      <MetricCard label="覆盖" :value="formatNumber(catalog?.OverrideCount)" help="已持久化的覆盖贴图数量。" />
      <MetricCard label="最近扫描" :value="scannedText" help="最近一次完成贴图扫描的时间。" />
    </div>

    <SectionPanel title="贴图包" :icon="Images">
      <div class="texture-toolbar">
        <input id="importTextureFile" ref="importFile" class="hidden-file-input" type="file" accept=".zip,application/zip" @change="importTextures">
        <button id="importTextures" class="secondary" type="button" :disabled="importing" @click="openImportPicker">
          <Upload class="button-icon" />
          {{ importing ? "导入中..." : "导入贴图包" }}
        </button>
        <button id="clearTextureOverrides" class="secondary danger" type="button" :disabled="scanStarting || (catalog?.OverrideCount ?? 0) === 0" @click="clearTextureOverrides">
          <Trash2 class="button-icon" />
          清空覆盖
        </button>
        <span class="texture-scan-status" :class="{ active: scanStatus?.IsScanning }">{{ scanStatusText }}</span>
      </div>
      <div v-if="errors.length" class="texture-errors">
        <strong>扫描警告</strong>
        <span v-for="error in errors.slice(0, 6)" :key="error">{{ error }}</span>
      </div>
      <div v-if="operationErrors.length" class="texture-errors">
        <strong>操作警告</strong>
        <span v-for="error in operationErrors.slice(0, 6)" :key="error">{{ error }}</span>
      </div>
    </SectionPanel>

    <SectionPanel title="贴图目录" :icon="Layers3">
      <template #actions>
        <button class="secondary" type="button" :disabled="catalogLoading" @click="loadCatalog">
          <RefreshCw class="button-icon" />
          {{ catalogLoading ? "刷新中" : "刷新" }}
        </button>
      </template>

      <div class="texture-directory-tools">
        <label class="field texture-scene-filter">
          <span class="field-label">场景</span>
          <select id="textureSceneFilter" v-model="selectedScene">
            <option value="">全部场景</option>
            <option v-for="scene in scenes" :key="scene" :value="scene">{{ scene }}</option>
          </select>
        </label>
        <div class="texture-view-toggle" role="group" aria-label="贴图目录视图">
          <button class="secondary" :class="{ active: viewMode === 'list' }" type="button" @click="setViewMode('list')">
            <List class="button-icon" />
            列表
          </button>
          <button class="secondary" :class="{ active: viewMode === 'gallery' }" type="button" @click="setViewMode('gallery')">
            <Grid2X2 class="button-icon" />
            图库
          </button>
        </div>
      </div>

      <div v-if="!items.length" class="empty-state">
        <p>{{ catalogLoading ? "正在加载贴图目录。" : "当前没有贴图记录。" }}</p>
      </div>
      <div v-else-if="viewMode === 'gallery'" class="texture-gallery">
        <article v-for="item in items" :key="item.SourceHash" class="texture-card" :class="{ overridden: item.HasOverride }">
          <div class="texture-gallery-thumb">
            <img :src="textureImageUrl(item)" :alt="item.TextureName" loading="lazy" decoding="async">
          </div>
          <div class="texture-card-copy">
            <strong>{{ item.TextureName }}</strong>
            <span>{{ item.Width }} x {{ item.Height }}</span>
            <small>{{ item.HasOverride ? "已覆盖" : "原图" }}</small>
          </div>
        </article>
      </div>
      <div v-else class="texture-list">
        <article v-for="item in items" :key="item.SourceHash" class="texture-item" :class="{ overridden: item.HasOverride }">
          <div class="texture-thumb">
            <img :src="textureImageUrl(item)" :alt="item.TextureName" loading="lazy" decoding="async">
          </div>
          <div class="texture-main">
            <h3>{{ item.TextureName }}</h3>
            <p>{{ textureMeta(item) }}</p>
            <small>{{ primaryReference(item) }}</small>
            <code>{{ item.SourceHash }}</code>
          </div>
          <span class="texture-status">{{ item.HasOverride ? "已覆盖" : "原图" }}</span>
        </article>
      </div>

      <div class="texture-pager">
        <span>共 {{ formatNumber(filteredCount) }} 张，当前 {{ currentPage }} / {{ totalPages }} 页。</span>
        <div>
          <button class="secondary" type="button" :disabled="currentPage <= 1" @click="goToPage(currentPage - 1)">
            <ChevronLeft class="button-icon" />
            上一页
          </button>
          <button class="secondary" type="button" :disabled="currentPage >= totalPages" @click="goToPage(currentPage + 1)">
            下一页
            <ChevronRight class="button-icon" />
          </button>
        </div>
      </div>
    </SectionPanel>
  </section>
</template>
