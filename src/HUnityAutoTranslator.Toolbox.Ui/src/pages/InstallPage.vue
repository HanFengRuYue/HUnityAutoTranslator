<script setup lang="ts">
import { computed, ref } from "vue";
import {
  AlertTriangle,
  ArchiveRestore,
  Download,
  Gamepad2,
  PackageCheck,
  ShieldCheck
} from "lucide-vue-next";
import SectionPanel from "../components/SectionPanel.vue";
import { safeInvoke } from "../api/client";
import {
  requestNavigation,
  selectedGame as selectedGameFromStore,
  selectedInspection as selectedInspectionFromStore,
  toolboxStore,
  upsertGame
} from "../state/toolboxStore";
import type { InstallMode, InstallPlan } from "../types/api";

const installModeOptions: Array<{ value: InstallMode; title: string; detail: string; badge?: string }> = [
  { value: "Full", title: "完整安装", detail: "安装 BepInEx、插件和必要依赖。", badge: "推荐" },
  { value: "PluginOnly", title: "只更新插件", detail: "保留现有 BepInEx，只替换插件文件。" },
  { value: "LlamaCppBackendOnly", title: "仅安装本地模型后端", detail: "只写入 llama.cpp CUDA 或 Vulkan 后端包。" }
];

const packageVersion = ref("0.1.1");
const installMode = ref<InstallMode>("Full");
const includeLlamaCppBackend = ref(false);
const llamaCppBackend = ref("Cuda13");
const message = ref("安装页只使用游戏库当前选中的游戏。");

const selectedGame = computed(() => selectedGameFromStore());
const selectedInspection = computed(() => selectedInspectionFromStore());
const runtimeSummary = computed(() => selectedInspection.value
  ? `${selectedInspection.value.Backend} ${selectedInspection.value.Architecture}`
  : "未检测");

const installSteps = computed(() => [
  {
    title: "游戏库目标",
    detail: selectedGame.value?.Root || "先在游戏库添加并选择游戏目录。",
    status: selectedGame.value ? "已选择" : "未选择"
  },
  {
    title: "运行环境",
    detail: selectedInspection.value
      ? `BepInEx：${selectedInspection.value.BepInExVersion ?? "未安装"}，插件：${selectedInspection.value.PluginInstalled ? "已存在" : "未安装"}`
      : "在游戏库检测目录后显示运行时信息。",
    status: selectedInspection.value ? runtimeSummary.value : "待检测"
  },
  {
    title: "准备安装包",
    detail: "根据游戏运行时选择 Mono、IL2CPP 或 BepInEx5 包。",
    status: toolboxStore.installPlan?.PluginPackageName ?? "待准备"
  },
  {
    title: "预览文件变更",
    detail: "列出新增、覆盖、备份和受保护的本地数据。",
    status: toolboxStore.installPlan ? "可查看" : "待生成"
  }
]);

async function previewInstallPlan(): Promise<void> {
  if (!selectedGame.value) {
    message.value = "请先到游戏库添加并选择游戏目录。";
    requestNavigation("library");
    return;
  }

  toolboxStore.isPlanningInstall = true;
  try {
    const plan = await safeInvoke<InstallPlan>("createInstallPlan", {
      gameRoot: selectedGame.value.Root,
      packageVersion: packageVersion.value,
      mode: installMode.value,
      includeLlamaCppBackend: includeLlamaCppBackend.value,
      llamaCppBackend: llamaCppBackend.value
    });
    if (plan) {
      toolboxStore.installPlan = plan;
      upsertGame({ ...selectedGame.value, Inspection: plan.Inspection, UpdatedUtc: new Date().toISOString() });
      message.value = `安装计划已生成：${plan.PluginPackageName}`;
    }
  } finally {
    toolboxStore.isPlanningInstall = false;
  }
}
</script>

<template>
  <section class="page install-page">
    <header class="page-hero">
      <div>
        <span class="eyebrow">{{ selectedGame ? selectedGame.Name : "未选择游戏" }}</span>
        <h1>自动安装</h1>
        <p>{{ selectedGame ? selectedGame.Root : "游戏库不会默认选择你电脑里的任何目录。" }}</p>
      </div>
      <div class="status-strip">
        <span class="pill">{{ runtimeSummary }}</span>
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
      <SectionPanel title="安装目标" description="将根据游戏库当前选中的游戏生成安装计划。">
        <template #actions>
          <button class="button-primary" type="button" :disabled="toolboxStore.isPlanningInstall" @click="previewInstallPlan">
            <PackageCheck class="icon" />{{ toolboxStore.isPlanningInstall ? "生成中" : "生成安装预览" }}
          </button>
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

      <SectionPanel title="安装模式" description="选择本次安装要写入的内容。">
        <div class="choice-grid">
          <button
            v-for="option in installModeOptions"
            :key="option.value"
            class="choice"
            :class="{ active: installMode === option.value }"
            type="button"
            @click="installMode = option.value"
          >
            <span v-if="option.badge" class="badge">{{ option.badge }}</span>
            <strong>{{ option.title }}</strong>
            <span>{{ option.detail }}</span>
          </button>
        </div>
        <div class="inline-settings">
          <label class="check"><input v-model="includeLlamaCppBackend" type="checkbox">同时准备 llama.cpp 后端包</label>
          <label class="field compact-field">
            <span>后端类型</span>
            <select v-model="llamaCppBackend" :disabled="!includeLlamaCppBackend">
              <option value="Cuda13">CUDA 13</option>
              <option value="Vulkan">Vulkan</option>
            </select>
          </label>
        </div>
      </SectionPanel>

      <SectionPanel title="预安装核对" description="检查游戏目录、运行时和安装包的准备状态。">
        <div class="steps">
          <div v-for="(step, index) in installSteps" :key="step.title" class="step">
            <div class="step-index">{{ index + 1 }}</div>
            <div>
              <strong>{{ step.title }}</strong>
              <span>{{ step.detail }}</span>
            </div>
            <span class="badge">{{ step.status }}</span>
          </div>
        </div>
        <p class="message">{{ message }}</p>
      </SectionPanel>

      <SectionPanel v-if="toolboxStore.installPlan" title="文件变更预览" description="点击执行安装前请核对受影响的目录。">
        <template #actions>
          <button class="button-primary" type="button"><Download class="icon" />执行安装</button>
          <button type="button"><ShieldCheck class="icon" />查看受保护数据</button>
          <button type="button"><ArchiveRestore class="icon" />回滚上次安装</button>
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
              <tr v-for="operation in toolboxStore.installPlan.Operations" :key="`${operation.Kind}-${operation.DestinationPath}`">
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
