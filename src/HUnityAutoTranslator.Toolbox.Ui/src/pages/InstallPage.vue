<script setup lang="ts">
import { computed, reactive, ref } from "vue";
import {
  AlertTriangle,
  ArchiveRestore,
  CheckCircle2,
  Circle,
  Download,
  FolderOpen,
  Gamepad2,
  Info,
  Loader2,
  MinusCircle,
  PackageOpen,
  Play,
  RefreshCcw,
  Rocket,
  ShieldCheck,
  Sparkles,
  X,
  XCircle
} from "lucide-vue-next";
import SectionPanel from "../components/SectionPanel.vue";
import { safeInvoke } from "../api/client";
import { invokeToolbox } from "../bridge";
import {
  cancelCurrentInstall,
  countNonDefaultCustomOptions,
  defaultCustomInstallOptions,
  loadEmbeddedBundleInfo,
  requestNavigation,
  resetInstallRun,
  rollbackCurrentInstall,
  selectedGame as selectedGameFromStore,
  selectedInspection as selectedInspectionFromStore,
  startInstall,
  toolboxStore,
  upsertGame
} from "../state/toolboxStore";
import type {
  BackupPolicy,
  BepInExHandling,
  CustomInstallOptions,
  EmbeddedAssetInfo,
  InstallMode,
  InstallPlan
} from "../types/api";

interface ModeChoice {
  value: InstallMode;
  title: string;
  detail: string;
  badge?: string;
}

const modeChoices: ModeChoice[] = [
  { value: "Full", title: "完整安装", detail: "安装 BepInEx 框架、插件 DLL 及依赖。", badge: "推荐" },
  { value: "PluginOnly", title: "只更新插件", detail: "保留现有 BepInEx,只替换插件文件。" },
  { value: "LlamaCppBackendOnly", title: "仅安装本地模型后端", detail: "只写入 llama.cpp 后端包,不动其他文件。" }
];

const packageVersion = ref("0.1.1");
const customOpen = ref(false);
const customOptions = reactive<CustomInstallOptions>(defaultCustomInstallOptions());

const selectedGame = computed(() => selectedGameFromStore());
const selectedInspection = computed(() => selectedInspectionFromStore());
const installRun = computed(() => toolboxStore.installRun);
const embeddedBundle = computed<EmbeddedAssetInfo[]>(() => toolboxStore.embeddedBundle ?? []);
const customChangedCount = computed(() => countNonDefaultCustomOptions(customOptions));
const isRunning = computed(() => installRun.value?.status === "running");
const canStart = computed(() => Boolean(selectedGame.value) && !isRunning.value && bundleReady.value);
const bundleReady = computed(() => embeddedBundle.value.length > 0);

const recommendedRuntimeLabel = computed(() => {
  const runtime = selectedInspection.value?.RecommendedRuntime;
  switch (runtime) {
    case "BepInEx5Mono": return "BepInEx 5 (Mono)";
    case "Mono": return "BepInEx 6 (Mono)";
    case "IL2CPP": return "BepInEx 6 (IL2CPP)";
    default: return "未检测";
  }
});

const effectiveRuntimeLabel = computed(() => {
  if (customOptions.runtimeOverride) {
    return runtimeDisplay(customOptions.runtimeOverride);
  }
  return recommendedRuntimeLabel.value;
});

function runtimeDisplay(runtime: string): string {
  switch (runtime) {
    case "BepInEx5Mono": return "BepInEx 5 (Mono)";
    case "Mono": return "BepInEx 6 (Mono)";
    case "IL2CPP": return "BepInEx 6 (IL2CPP)";
    default: return runtime;
  }
}

const heroSubline = computed(() => {
  if (!selectedGame.value) return "请先在游戏库添加并选择 Unity 游戏目录。";
  if (!bundleReady.value) return "当前是开发模式构建,未包含内置离线资源 — 请运行 build/package-toolbox.ps1 重新打包。";
  const runtime = effectiveRuntimeLabel.value;
  const llama = customOptions.includeLlamaCppBackend ? `,附带 llama.cpp ${customOptions.llamaCppBackend} 后端` : ",不包含 llama.cpp";
  if (customOptions.dryRun) {
    return `干跑模式:仅显示计划,不会真正写入任何文件。${runtime}${llama}`;
  }
  return `将自动安装 ${runtime} 框架与插件${llama}。`;
});

function bundleSizeLabel(bytes: number): string {
  if (!bytes) return "-";
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  return `${(bytes / 1024 / 1024 / 1024).toFixed(2)} GB`;
}

function stageLabel(stage: string): string {
  switch (stage) {
    case "Preparing": return "准备中";
    case "Backup": return "备份";
    case "ExtractFramework": return "解压 BepInEx";
    case "ExtractPlugin": return "解压插件";
    case "ExtractLlamaCpp": return "解压 llama.cpp";
    case "PrepareUnityLibs": return "准备 Unity 基础库";
    case "Verify": return "校验";
    case "Completed": return "已完成";
    case "Failed": return "失败";
    case "Cancelled": return "已取消";
    case "Rollback": return "回滚";
    default: return stage;
  }
}

async function startOneClickInstall(): Promise<void> {
  if (!selectedGame.value) {
    requestNavigation("library");
    return;
  }
  const ok = await startInstall(selectedGame.value.Root, customOptions, packageVersion.value);
  if (ok && installRun.value) {
    upsertGame({ ...selectedGame.value, Inspection: installRun.value.plan.Inspection, UpdatedUtc: new Date().toISOString() });
  }
}

async function previewInstallPlan(): Promise<void> {
  if (!selectedGame.value) return;
  toolboxStore.isPlanningInstall = true;
  try {
    const plan = await safeInvoke<InstallPlan>("createInstallPlan", buildPlanPayload(selectedGame.value.Root));
    if (plan) {
      toolboxStore.installPlan = plan;
      upsertGame({ ...selectedGame.value, Inspection: plan.Inspection, UpdatedUtc: new Date().toISOString() });
    }
  } finally {
    toolboxStore.isPlanningInstall = false;
  }
}

function buildPlanPayload(gameRoot: string): Record<string, unknown> {
  return {
    gameRoot,
    packageVersion: packageVersion.value,
    mode: customOptions.mode,
    includeLlamaCppBackend: customOptions.includeLlamaCppBackend,
    llamaCppBackend: customOptions.llamaCppBackend,
    runtimeOverride: customOptions.runtimeOverride || "",
    bepInExHandling: customOptions.bepInExHandling,
    backupPolicy: customOptions.backupPolicy,
    customPluginDirectory: customOptions.customPluginDirectory,
    customBackupDirectory: customOptions.customBackupDirectory,
    customPluginZipPath: customOptions.customPluginZipPath,
    customBepInExZipPath: customOptions.customBepInExZipPath,
    customLlamaCppZipPath: customOptions.customLlamaCppZipPath,
    customUnityLibraryZipPath: customOptions.customUnityLibraryZipPath,
    unityVersionOverride: customOptions.unityVersionOverride,
    dryRun: customOptions.dryRun,
    forceReinstall: customOptions.forceReinstall,
    skipPostInstallVerification: customOptions.skipPostInstallVerification
  };
}

async function browseFile(target: keyof CustomInstallOptions, title: string): Promise<void> {
  const result = await invokeToolbox<{ Status: string; FilePath: string | null }>("pickFile", {
    title,
    filter: "ZIP 文件 (*.zip)|*.zip|所有文件 (*.*)|*.*"
  });
  if (result?.FilePath) {
    (customOptions as Record<string, unknown>)[target] = result.FilePath;
  }
}

async function browseDirectory(target: keyof CustomInstallOptions): Promise<void> {
  const result = await invokeToolbox<string>("pickGameDirectory", { initialDirectory: selectedGame.value?.Root ?? "" });
  if (result) {
    (customOptions as Record<string, unknown>)[target] = result;
  }
}

function resetCustomOptions(): void {
  Object.assign(customOptions, defaultCustomInstallOptions());
}

function refreshBundle(): void {
  void loadEmbeddedBundleInfo();
}
</script>

<template>
  <section class="page install-page">
    <header class="page-hero">
      <div>
        <span class="eyebrow">{{ selectedGame ? selectedGame.Name : "未选择游戏" }}</span>
        <h1>一键安装</h1>
        <p>{{ selectedGame ? selectedGame.Root : "游戏库不会默认选择你电脑里的任何目录。" }}</p>
      </div>
      <div class="status-strip">
        <span class="pill">{{ effectiveRuntimeLabel }}</span>
        <span class="pill">{{ selectedInspection?.BepInExVersion ?? "BepInEx 未检测" }}</span>
        <span class="pill">{{ selectedInspection?.PluginInstalled ? "插件已存在" : "插件未安装" }}</span>
      </div>
    </header>

    <div v-if="!selectedGame" class="panel empty-state">
      <AlertTriangle class="empty-icon" />
      <strong>没有安装目标</strong>
      <span>请先在游戏库添加并选择游戏目录。</span>
      <button class="button-primary" type="button" @click="requestNavigation('library')">
        <Gamepad2 class="icon" />打开游戏库
      </button>
    </div>

    <template v-else>
      <SectionPanel title="安装目标" description="本页固定使用游戏库当前选中的游戏。">
        <template #actions>
          <button type="button" @click="requestNavigation('library')"><Gamepad2 class="icon" />切换游戏</button>
        </template>
        <div class="install-target">
          <div>
            <strong>{{ selectedGame.Name }}</strong>
            <p>{{ selectedGame.Root }}</p>
          </div>
          <label class="field compact-field">
            <span>插件版本</span>
            <input v-model="packageVersion" spellcheck="false">
          </label>
        </div>
      </SectionPanel>

      <SectionPanel title="开始" description="按预设方案自动执行;详细行为见下方副标题。">
        <div class="hero-cta">
          <button
            class="button-primary install-hero-button"
            type="button"
            :disabled="!canStart"
            @click="startOneClickInstall"
          >
            <Rocket class="icon" />{{ isRunning ? "安装中..." : "开始一键安装" }}
          </button>
          <p class="hero-subline">{{ heroSubline }}</p>
          <div v-if="!bundleReady" class="warn-banner">
            <AlertTriangle class="icon" />
            未检测到内置资源。运行 <code>build/package-toolbox.ps1</code> 重新打包后再试,或点
            <button type="button" class="link-button" @click="refreshBundle">刷新资源信息</button>。
          </div>
        </div>
      </SectionPanel>

      <details class="custom-install" :open="customOpen" @toggle="customOpen = ($event.target as HTMLDetailsElement).open">
        <summary>
          <Sparkles class="icon" /> 自定义安装(高级)
          <span v-if="customChangedCount > 0" class="badge changed-badge">{{ customChangedCount }} 项已修改</span>
        </summary>

        <SectionPanel title="基础" description="选择本次写入的内容。">
          <div class="choice-grid">
            <button
              v-for="choice in modeChoices"
              :key="choice.value"
              class="choice"
              :class="{ active: customOptions.mode === choice.value }"
              type="button"
              @click="customOptions.mode = choice.value"
            >
              <span v-if="choice.badge" class="badge">{{ choice.badge }}</span>
              <strong>{{ choice.title }}</strong>
              <span>{{ choice.detail }}</span>
            </button>
          </div>
          <div class="inline-settings">
            <label class="check">
              <input v-model="customOptions.includeLlamaCppBackend" type="checkbox">
              同时安装 llama.cpp 后端
            </label>
            <label class="field compact-field">
              <span>后端类型</span>
              <select v-model="customOptions.llamaCppBackend" :disabled="!customOptions.includeLlamaCppBackend">
                <option value="Cuda13">CUDA 13</option>
                <option value="Vulkan">Vulkan</option>
              </select>
            </label>
          </div>

          <div class="bundle-table">
            <div class="bundle-header"><PackageOpen class="icon" />内置资源</div>
            <ul>
              <li v-for="asset in embeddedBundle" :key="asset.Key">
                <strong>{{ asset.Kind }} / {{ asset.Runtime === 'Unknown' ? asset.Backend : asset.Runtime }}</strong>
                <span>{{ asset.Version }} · {{ bundleSizeLabel(asset.SizeBytes) }}</span>
              </li>
              <li v-if="!embeddedBundle.length" class="muted">未加载,请重新打包工具箱。</li>
            </ul>
          </div>
        </SectionPanel>

        <SectionPanel title="高级" description="覆盖默认目录与策略。普通用户保持默认即可。">
          <div class="grid-two">
            <label class="field">
              <span>运行时覆盖</span>
              <select v-model="customOptions.runtimeOverride">
                <option value="">自动检测(推荐)</option>
                <option value="IL2CPP">BepInEx 6 IL2CPP</option>
                <option value="Mono">BepInEx 6 Mono</option>
                <option value="BepInEx5Mono">BepInEx 5 Mono</option>
              </select>
            </label>
            <label class="field">
              <span>BepInEx 框架</span>
              <select v-model="(customOptions.bepInExHandling as BepInExHandling)">
                <option value="Auto">按需安装(默认)</option>
                <option value="Always">强制重装框架</option>
                <option value="Skip">不动框架</option>
              </select>
            </label>
            <label class="field">
              <span>备份策略</span>
              <select v-model="(customOptions.backupPolicy as BackupPolicy)">
                <option value="Auto">按需备份(默认)</option>
                <option value="Always">总是备份</option>
                <option value="Skip">不备份(不推荐)</option>
              </select>
            </label>
            <label class="field">
              <span>自定义插件目录</span>
              <div class="input-with-button">
                <input v-model="customOptions.customPluginDirectory" placeholder="默认 BepInEx/plugins/HUnityAutoTranslator" spellcheck="false">
                <button type="button" @click="browseDirectory('customPluginDirectory')"><FolderOpen class="icon" /></button>
              </div>
            </label>
            <label class="field">
              <span>自定义备份目录</span>
              <div class="input-with-button">
                <input v-model="customOptions.customBackupDirectory" placeholder="默认 BepInEx/config/HUnityAutoTranslator/toolbox-backups/&lt;时间戳&gt;" spellcheck="false">
                <button type="button" @click="browseDirectory('customBackupDirectory')"><FolderOpen class="icon" /></button>
              </div>
            </label>
          </div>

          <div class="inline-settings">
            <label class="check">
              <input v-model="customOptions.forceReinstall" type="checkbox">
              强制重装(即使插件已是同版本)
            </label>
            <label class="check">
              <input v-model="customOptions.dryRun" type="checkbox">
              干跑(只显示计划,不修改任何文件)
            </label>
          </div>

          <p class="hint"><Info class="icon" />插件配置始终位于标准的 <code>BepInEx/config/HUnityAutoTranslator/</code> 路径(由 BepInEx 决定),即使插件 DLL 放到自定义目录也不影响。</p>
        </SectionPanel>

        <SectionPanel title="开发者" description="仅在调试时使用 — 用本地 zip 替换内置资源。">
          <div class="grid-two">
            <label class="field">
              <span>自定义插件 zip</span>
              <div class="input-with-button">
                <input v-model="customOptions.customPluginZipPath" placeholder="留空则使用内置" spellcheck="false">
                <button type="button" @click="browseFile('customPluginZipPath', '选择插件 zip')"><FolderOpen class="icon" /></button>
              </div>
            </label>
            <label class="field">
              <span>自定义 BepInEx zip</span>
              <div class="input-with-button">
                <input v-model="customOptions.customBepInExZipPath" placeholder="留空则使用内置" spellcheck="false">
                <button type="button" @click="browseFile('customBepInExZipPath', '选择 BepInEx zip')"><FolderOpen class="icon" /></button>
              </div>
            </label>
            <label class="field">
              <span>自定义 llama.cpp zip</span>
              <div class="input-with-button">
                <input v-model="customOptions.customLlamaCppZipPath" placeholder="留空则使用内置" spellcheck="false">
                <button type="button" @click="browseFile('customLlamaCppZipPath', '选择 llama.cpp zip')"><FolderOpen class="icon" /></button>
              </div>
            </label>
            <label class="field">
              <span>自定义 Unity 库 zip(IL2CPP)</span>
              <div class="input-with-button">
                <input v-model="customOptions.customUnityLibraryZipPath" placeholder="留空则自动从全局缓存或网络获取" spellcheck="false">
                <button type="button" @click="browseFile('customUnityLibraryZipPath', '选择 Unity 库 zip')"><FolderOpen class="icon" /></button>
              </div>
            </label>
            <label class="field">
              <span>Unity 版本覆盖</span>
              <input v-model="customOptions.unityVersionOverride" placeholder="如 2022.3.21f1,留空则自动检测" spellcheck="false">
            </label>
            <label class="check">
              <input v-model="customOptions.skipPostInstallVerification" type="checkbox">
              跳过安装后校验
            </label>
          </div>
          <div class="custom-actions">
            <button type="button" @click="resetCustomOptions"><RefreshCcw class="icon" />重置为默认</button>
            <button class="button-primary" type="button" :disabled="toolboxStore.isPlanningInstall" @click="previewInstallPlan">
              <Play class="icon" />预览安装计划(不执行)
            </button>
          </div>
        </SectionPanel>
      </details>

      <SectionPanel v-if="installRun" title="安装进度" :description="`运行 ID: ${installRun.id.slice(0, 8)}...`">
        <template #actions>
          <button v-if="installRun.status === 'running'" type="button" @click="cancelCurrentInstall"><X class="icon" />取消</button>
          <button v-if="installRun.status === 'failed' || installRun.status === 'cancelled'" type="button" :disabled="toolboxStore.isRollingBack" @click="selectedGame && rollbackCurrentInstall(selectedGame.Root)">
            <ArchiveRestore class="icon" />回滚到上次备份
          </button>
          <button v-if="installRun.status !== 'running'" type="button" @click="resetInstallRun"><RefreshCcw class="icon" />关闭</button>
        </template>

        <div class="run-state-line" :class="`install-state-${installRun.status}`">
          <strong>{{ stageLabel(installRun.progress.stage) }}</strong>
          <span>{{ installRun.progress.message }}</span>
        </div>

        <div class="install-progress-bar">
          <div class="install-progress-fill" :style="{ width: `${Math.round(installRun.progress.percent * 100)}%` }"></div>
        </div>

        <ol class="steps">
          <li
            v-for="(operation, index) in installRun.plan.Operations"
            :key="`${operation.Kind}-${index}`"
            class="step"
            :class="`step-${installRun.perStepStatus[index]}`"
          >
            <div class="step-icon">
              <Loader2 v-if="installRun.perStepStatus[index] === 'running'" class="icon icon-spin" />
              <CheckCircle2 v-else-if="installRun.perStepStatus[index] === 'done'" class="icon" />
              <XCircle v-else-if="installRun.perStepStatus[index] === 'failed'" class="icon" />
              <MinusCircle v-else-if="installRun.perStepStatus[index] === 'skipped'" class="icon" />
              <Circle v-else class="icon" />
            </div>
            <div class="step-body">
              <strong>{{ operation.Description }}</strong>
              <span>{{ operation.DestinationPath }}</span>
            </div>
            <span class="badge">{{ operation.Kind }}</span>
          </li>
        </ol>

        <div v-if="installRun.status === 'succeeded'" class="install-result install-state-succeeded">
          <CheckCircle2 class="icon" />
          <strong>安装成功!</strong>
          <span>{{ installRun.result?.Message ?? "" }}</span>
        </div>
        <div v-if="installRun.status === 'failed'" class="install-result install-state-failed">
          <XCircle class="icon" />
          <strong>安装失败</strong>
          <span>{{ installRun.error?.message ?? "" }}</span>
        </div>
        <div v-if="installRun.status === 'cancelled'" class="install-result install-state-cancelled">
          <Info class="icon" />
          <strong>安装已取消</strong>
          <span>备份保留在: {{ installRun.error?.backupDirectory ?? installRun.plan.BackupDirectory }}</span>
        </div>
      </SectionPanel>

      <SectionPanel title="受保护的本地数据" description="安装期间不会覆盖以下文件 — 翻译缓存、Provider 凭据等会原样保留。">
        <template #actions>
          <ShieldCheck class="icon" />
        </template>
        <ul class="protected-list">
          <li v-for="path in selectedInspection?.ProtectedDataPaths ?? []" :key="path">
            <ShieldCheck class="icon protected-icon" />
            <span>{{ path }}</span>
          </li>
          <li v-if="!(selectedInspection?.ProtectedDataPaths?.length)" class="muted">游戏目录尚无用户数据需要保护。</li>
        </ul>
      </SectionPanel>

      <SectionPanel v-if="toolboxStore.installPlan && !installRun" title="文件变更预览" description="点击执行安装前的最后核对。">
        <template #actions>
          <button class="button-primary" type="button" :disabled="!canStart" @click="startOneClickInstall">
            <Download class="icon" />执行安装
          </button>
        </template>
        <div class="table-wrap">
          <table class="data-table">
            <thead>
              <tr>
                <th>类型</th>
                <th>来源</th>
                <th>目标</th>
                <th>说明</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(operation, index) in toolboxStore.installPlan.Operations" :key="`${operation.Kind}-${index}`">
                <td>{{ operation.Kind }}</td>
                <td>{{ operation.SourcePath || "-" }}</td>
                <td>{{ operation.DestinationPath }}</td>
                <td>{{ operation.Description }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </SectionPanel>
    </template>
  </section>
</template>

<style scoped>
.install-page .hero-cta {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.install-hero-button {
  align-self: flex-start;
  font-size: 1.05rem;
  padding: 0.85rem 1.6rem;
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.hero-subline {
  margin: 0;
  color: var(--ink-muted, #93a5c5);
}

.warn-banner {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 0.8rem;
  border-radius: 8px;
  background: rgba(255, 196, 86, 0.12);
  color: #ffd88c;
  font-size: 0.9rem;
}

.warn-banner code {
  background: rgba(255, 255, 255, 0.1);
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
}

.link-button {
  background: none;
  border: none;
  color: inherit;
  text-decoration: underline;
  cursor: pointer;
  padding: 0;
  font-size: inherit;
}

.custom-install {
  border-radius: 14px;
  border: 1px solid var(--surface-border, rgba(255, 255, 255, 0.08));
  padding: 0.4rem 0.85rem 0.85rem;
}

.custom-install > summary {
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.6rem 0.2rem;
  font-weight: 600;
}

.custom-install > summary .changed-badge {
  margin-left: auto;
  background: rgba(108, 184, 255, 0.18);
  color: #6cb8ff;
}

.custom-install :deep(.section-panel) {
  margin-top: 0.6rem;
}

.bundle-table {
  margin-top: 0.8rem;
  border-radius: 10px;
  background: rgba(255, 255, 255, 0.03);
  padding: 0.6rem 0.8rem;
}

.bundle-header {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
}

.bundle-table ul {
  list-style: none;
  margin: 0;
  padding: 0;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.3rem 1rem;
  font-size: 0.85rem;
}

.bundle-table li {
  display: flex;
  flex-direction: column;
}

.bundle-table .muted {
  grid-column: 1 / -1;
  color: var(--ink-muted, #93a5c5);
}

.grid-two {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 0.8rem;
}

@media (max-width: 720px) {
  .grid-two {
    grid-template-columns: 1fr;
  }
}

.input-with-button {
  display: flex;
  gap: 0.4rem;
}

.input-with-button input {
  flex: 1;
}

.input-with-button button {
  padding: 0 0.6rem;
}

.hint {
  margin-top: 0.7rem;
  display: flex;
  align-items: flex-start;
  gap: 0.4rem;
  color: var(--ink-muted, #93a5c5);
  font-size: 0.85rem;
}

.hint code {
  background: rgba(255, 255, 255, 0.06);
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
}

.custom-actions {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  margin-top: 0.8rem;
}

.install-progress-bar {
  width: 100%;
  height: 8px;
  background: rgba(255, 255, 255, 0.08);
  border-radius: 6px;
  overflow: hidden;
  margin: 0.6rem 0;
}

.install-progress-fill {
  height: 100%;
  background: linear-gradient(90deg, #6cb8ff, #b48bff);
  transition: width 0.25s ease-out;
}

.run-state-line {
  display: flex;
  align-items: baseline;
  gap: 0.5rem;
}

.run-state-line strong {
  font-size: 0.95rem;
}

.run-state-line span {
  color: var(--ink-muted, #93a5c5);
  font-size: 0.85rem;
}

.steps {
  list-style: none;
  margin: 0.4rem 0 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.step {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  padding: 0.5rem 0.7rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 8px;
  font-size: 0.88rem;
}

.step-icon {
  flex-shrink: 0;
}

.step-body {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-width: 0;
}

.step-body span {
  color: var(--ink-muted, #93a5c5);
  font-size: 0.78rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.step-pending { opacity: 0.6; }
.step-running { background: rgba(108, 184, 255, 0.1); }
.step-done .step-icon { color: #56d97a; }
.step-failed .step-icon { color: #ff8c8c; }
.step-skipped { opacity: 0.45; }

.icon-spin {
  animation: install-spin 1s linear infinite;
}

@keyframes install-spin {
  to { transform: rotate(360deg); }
}

.install-result {
  margin-top: 0.6rem;
  padding: 0.7rem 0.9rem;
  border-radius: 10px;
  display: flex;
  align-items: center;
  gap: 0.6rem;
  font-size: 0.95rem;
}

.install-state-succeeded { background: rgba(86, 217, 122, 0.12); color: #b6f0c5; }
.install-state-failed { background: rgba(255, 140, 140, 0.12); color: #ffc2c2; }
.install-state-cancelled { background: rgba(255, 196, 86, 0.12); color: #ffd88c; }
.install-state-rollback { background: rgba(108, 184, 255, 0.12); color: #b9d8ff; }

.protected-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.3rem;
  font-size: 0.85rem;
}

.protected-list li {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  padding: 0.3rem 0.5rem;
  background: rgba(255, 255, 255, 0.03);
  border-radius: 6px;
}

.protected-icon {
  color: #56d97a;
}

.muted {
  color: var(--ink-muted, #93a5c5);
}
</style>
