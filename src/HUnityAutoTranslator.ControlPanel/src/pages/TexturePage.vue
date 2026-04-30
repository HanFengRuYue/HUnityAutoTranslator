<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import {
  Download,
  Images,
  Layers3,
  ScanLine,
  Trash2,
  Upload
} from "lucide-vue-next";
import { api, getJson } from "../api/client";
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

const catalog = ref<TextureCatalogPage | null>(null);
const loading = ref(false);
const importing = ref(false);
const exporting = ref(false);
const importFile = ref<HTMLInputElement | null>(null);
const operationErrors = ref<string[]>([]);

const items = computed(() => catalog.value?.Items ?? []);
const errors = computed(() => catalog.value?.Errors ?? []);
const scannedText = computed(() => catalog.value?.ScannedUtc ? formatDateTime(catalog.value.ScannedUtc) : "尚未扫描");

async function loadCatalog(): Promise<void> {
  catalog.value = await getJson<TextureCatalogPage>("/api/textures");
}

async function scanTextures(): Promise<void> {
  loading.value = true;
  operationErrors.value = [];
  try {
    const result = await api<TextureScanResult>("/api/textures/scan", { method: "POST" });
    await loadCatalog();
    showToast(`贴图扫描完成：${result.TextureCount} 张，${result.ReferenceCount} 个引用。`, result.Errors.length ? "warn" : "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "贴图扫描失败。", "error");
  } finally {
    loading.value = false;
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
    const response = await fetch("/api/textures/export", { cache: "no-store" });
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
    const message = `已导入 ${result.ImportedCount} 张贴图，已应用 ${result.AppliedCount} 个引用。`;
    showToast(message, result.Errors.length ? "warn" : "ok");
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

  loading.value = true;
  operationErrors.value = [];
  try {
    const result = await api<TextureOverrideClearResult>("/api/textures/overrides", { method: "DELETE" });
    await loadCatalog();
    operationErrors.value = [...result.Errors];
    showToast(`已清空 ${result.DeletedCount} 个覆盖文件，已恢复 ${result.RestoredCount} 个引用。`, result.Errors.length ? "warn" : "ok");
  } catch (error) {
    showToast(error instanceof Error ? error.message : "清空贴图覆盖失败。", "error");
  } finally {
    loading.value = false;
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

onMounted(() => {
  void loadCatalog();
});
</script>

<template>
  <section class="page active" id="page-textures">
    <div class="page-head">
      <div>
        <h1>贴图替换</h1>
        <p>导出当前场景贴图包，并把修改后的 PNG 持久化替换回游戏。</p>
      </div>
      <div class="actions">
        <button id="scanTextures" class="secondary" type="button" :disabled="loading" @click="scanTextures"><ScanLine class="button-icon" />{{ loading ? "扫描中..." : "扫描贴图" }}</button>
        <button id="exportTextures" class="primary" type="button" :disabled="exporting" @click="exportTextures"><Download class="button-icon" />{{ exporting ? "导出中..." : "导出贴图包" }}</button>
      </div>
    </div>

    <div class="texture-summary">
      <MetricCard label="贴图" :value="formatNumber(catalog?.TextureCount)" help="去重后的 PNG 贴图数量。" />
      <MetricCard label="引用" :value="formatNumber(catalog?.ReferenceCount)" help="当前扫描到的场景贴图引用数量。" />
      <MetricCard label="覆盖" :value="formatNumber(catalog?.OverrideCount)" help="已持久化的覆盖贴图数量。" />
      <MetricCard label="最近扫描" :value="scannedText" help="本次运行中最近一次贴图扫描时间。" />
    </div>

    <SectionPanel title="贴图包" :icon="Images">
      <div class="texture-toolbar">
        <input id="importTextureFile" ref="importFile" class="hidden-file-input" type="file" accept=".zip,application/zip" @change="importTextures">
        <button id="importTextures" class="secondary" type="button" :disabled="importing" @click="openImportPicker"><Upload class="button-icon" />{{ importing ? "导入中..." : "导入贴图包" }}</button>
        <button id="clearTextureOverrides" class="secondary danger" type="button" :disabled="loading || (catalog?.OverrideCount ?? 0) === 0" @click="clearTextureOverrides"><Trash2 class="button-icon" />清空覆盖</button>
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
      <div v-if="!items.length" class="empty-state">
        <p>当前没有贴图记录。</p>
      </div>
      <div v-else class="texture-list">
        <article v-for="item in items" :key="item.SourceHash" class="texture-item" :class="{ overridden: item.HasOverride }">
          <div class="texture-thumb"><Images /></div>
          <div class="texture-main">
            <h3>{{ item.TextureName }}</h3>
            <p>{{ item.Width }} x {{ item.Height }} · {{ item.Format }} · {{ item.ReferenceCount }} 个引用</p>
            <small>{{ primaryReference(item) }}</small>
            <code>{{ item.SourceHash }}</code>
          </div>
          <span class="texture-status">{{ item.HasOverride ? "已覆盖" : "原图" }}</span>
        </article>
      </div>
    </SectionPanel>
  </section>
</template>
