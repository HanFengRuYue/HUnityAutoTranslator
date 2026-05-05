<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import {
  ArchiveRestore,
  BookOpen,
  CheckCircle2,
  Database,
  Download,
  FolderOpen,
  History,
  Info,
  MonitorCog,
  Moon,
  PackageCheck,
  Palette,
  Plug,
  RotateCcw,
  Settings,
  ShieldCheck,
  Sun,
  Wrench
} from "lucide-vue-next";
import brandIcon from "../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import { invokeToolbox } from "./bridge";
import type { DatabaseMaintenanceResult, GameInspection, InstallPlan, PageKey, ThemeMode } from "./types";

const themeStorageKey = "hunity.toolbox.theme";
const defaultGameRoot = "D:\\Game\\The Glitched Attraction";

const pages: Array<{ key: PageKey; label: string; icon: typeof Download }> = [
  { key: "install", label: "自动安装", icon: Download },
  { key: "config", label: "插件配置", icon: Settings },
  { key: "translations", label: "译文编辑", icon: Database },
  { key: "history", label: "安装记录", icon: History },
  { key: "about", label: "关于工具箱", icon: Info }
];

const activePage = ref<PageKey>("install");
const theme = ref<ThemeMode>(loadTheme());
const gameRoot = ref(defaultGameRoot);
const packageVersion = ref("0.1.1");
const includeLlamaCppBackend = ref(false);
const llamaCppBackend = ref("Cuda13");
const inspection = ref<GameInspection | null>(null);
const installPlan = ref<InstallPlan | null>(null);
const maintenanceResult = ref<DatabaseMaintenanceResult | null>(null);
const statusMessage = ref("选择游戏目录后先检测，再预览安装计划。");
const configForm = reactive({
  targetLanguage: "zh-Hans",
  httpPort: 48110,
  openPanelHotkey: "Alt+H",
  toggleTranslationHotkey: "Alt+F",
  providerCount: 0,
  fontReplacement: true,
  llamaModelPath: ""
});

const pageTitle = computed(() => pages.find((page) => page.key === activePage.value)?.label ?? "工具箱");
const themeText = computed(() => theme.value === "system" ? "跟随系统" : theme.value === "light" ? "浅色" : "深色");
const themeIcon = computed(() => theme.value === "system" ? MonitorCog : theme.value === "light" ? Sun : Moon);
const runtimeSummary = computed(() => {
  if (!inspection.value) {
    return "未检测";
  }

  return `${inspection.value.Backend} ${inspection.value.Architecture}`;
});
const installSteps = computed(() => [
  {
    title: "检测运行环境",
    detail: "识别 Mono / IL2CPP、x64 / x86、现有 BepInEx 和插件版本。",
    status: inspection.value ? "已完成" : "待检测"
  },
  {
    title: "准备安装包",
    detail: "优先使用本地发布包；没有时从官方来源下载并校验。",
    status: installPlan.value?.PluginPackageName ?? "待准备"
  },
  {
    title: "预览文件变更",
    detail: "列出新增、覆盖、备份和不会触碰的本地数据。",
    status: installPlan.value ? "可查看" : "待生成"
  },
  {
    title: "安装并验证",
    detail: "写入后检查目标文件、版本、目录结构和回滚点。",
    status: "待执行"
  }
]);

function loadTheme(): ThemeMode {
  const saved = localStorage.getItem(themeStorageKey);
  return saved === "system" || saved === "light" || saved === "dark" ? saved : "system";
}

function effectiveTheme(value: ThemeMode): "light" | "dark" {
  if (value === "system") {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  return value;
}

function applyTheme(): void {
  document.documentElement.dataset.theme = effectiveTheme(theme.value);
}

function cycleTheme(): void {
  const order: ThemeMode[] = ["system", "light", "dark"];
  theme.value = order[(order.indexOf(theme.value) + 1) % order.length];
  localStorage.setItem(themeStorageKey, theme.value);
  applyTheme();
}

async function inspectGame(): Promise<void> {
  inspection.value = await invokeToolbox<GameInspection>("inspectGame", { gameRoot: gameRoot.value });
  installPlan.value = null;
  statusMessage.value = inspection.value.IsValidUnityGame
    ? `已识别 ${inspection.value.GameName || "Unity 游戏"}，推荐运行时：${inspection.value.RecommendedRuntime}`
    : "没有识别到有效 Unity 游戏目录。";
}

async function previewInstallPlan(): Promise<void> {
  if (!inspection.value) {
    await inspectGame();
  }

  installPlan.value = await invokeToolbox<InstallPlan>("createInstallPlan", {
    gameRoot: gameRoot.value,
    packageVersion: packageVersion.value,
    mode: "Full",
    includeLlamaCppBackend: includeLlamaCppBackend.value,
    llamaCppBackend: llamaCppBackend.value
  });
  statusMessage.value = `安装计划已生成：${installPlan.value.PluginPackageName}`;
}

async function runMaintenance(): Promise<void> {
  maintenanceResult.value = await invokeToolbox<DatabaseMaintenanceResult>("runDatabaseMaintenance", {
    databasePath: `${gameRoot.value}\\BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite`,
    createBackup: true,
    runIntegrityCheck: true,
    reindex: true,
    vacuum: false
  });
}

watch(theme, applyTheme);

onMounted(() => {
  applyTheme();
  void inspectGame();
});
</script>

<template>
  <div class="shell">
    <aside class="sidebar">
      <div class="brand">
        <img class="brand-logo" :src="brandIcon" alt="HUnity" width="36" height="36">
        <div class="brand-copy">
          <strong>HUnity</strong>
          <span>外部工具箱</span>
        </div>
      </div>

      <nav class="nav-list" aria-label="工具箱导航">
        <button
          v-for="page in pages"
          :key="page.key"
          class="nav-item"
          :class="{ active: activePage === page.key }"
          type="button"
          :title="page.label"
          @click="activePage = page.key"
        >
          <component :is="page.icon" class="icon" aria-hidden="true" />
          <span>{{ page.label }}</span>
        </button>
      </nav>

      <div class="sidebar-footer">
        <span class="muted">本机单 exe</span>
        <button class="theme-cycle" type="button" :title="`主题：${themeText}`" @click="cycleTheme">
          <Palette class="icon" aria-hidden="true" />
          <span>主题</span>
          <strong>{{ themeText }}</strong>
          <component :is="themeIcon" class="icon" aria-hidden="true" />
        </button>
      </div>
    </aside>

    <section class="workspace">
      <header class="topbar">
        <div class="title-line">
          <img class="title-logo" :src="brandIcon" alt="HUnityAutoTranslator" width="44" height="44">
          <div class="title-copy">
            <span class="muted">{{ pageTitle }}</span>
            <strong>{{ activePage === "install" ? "安装 HUnityAutoTranslator" : pageTitle }}</strong>
          </div>
        </div>
        <div class="status-strip">
          <span class="pill">{{ runtimeSummary }}</span>
          <span class="pill">{{ inspection?.BepInExVersion ?? "BepInEx 未检测" }}</span>
          <span class="pill">{{ inspection?.PluginInstalled ? "插件已存在" : "插件未安装" }}</span>
        </div>
      </header>

      <main class="main">
        <section v-if="activePage === 'install'" class="page">
          <div class="panel">
            <h2>选择游戏和安装方案</h2>
            <div class="target-card">
              <div>
                <strong>{{ inspection?.GameName || "目标游戏目录" }}</strong>
                <div class="muted">{{ gameRoot }}</div>
              </div>
              <button type="button" @click="inspectGame">
                <FolderOpen class="icon" aria-hidden="true" />
                检测目录
              </button>
            </div>

            <div class="field-grid">
              <label class="field">
                <span>游戏根目录</span>
                <input v-model="gameRoot" spellcheck="false">
              </label>
              <label class="field">
                <span>插件版本</span>
                <input v-model="packageVersion" spellcheck="false">
              </label>
            </div>

            <div class="choice-grid" style="margin-top: 12px;">
              <div class="choice active">
                <span class="badge">推荐</span>
                <strong>完整安装</strong>
                <p class="muted">安装 BepInEx、插件和必要依赖。</p>
              </div>
              <div class="choice">
                <strong>只更新插件</strong>
                <p class="muted">保留现有 BepInEx，只替换 HUnityAutoTranslator。</p>
              </div>
              <div class="choice">
                <strong>安装本地模型后端</strong>
                <p class="muted">按需添加 llama.cpp CUDA 或 Vulkan 包。</p>
              </div>
            </div>

            <div class="steps">
              <div v-for="(step, index) in installSteps" :key="step.title" class="step">
                <div class="step-index">{{ index + 1 }}</div>
                <div>
                  <strong>{{ step.title }}</strong>
                  <div class="muted">{{ step.detail }}</div>
                </div>
                <span class="badge">{{ step.status }}</span>
              </div>
            </div>

            <div class="actions">
              <button class="button-primary" type="button" @click="previewInstallPlan">
                <PackageCheck class="icon" aria-hidden="true" />
                生成安装预览
              </button>
              <button type="button">
                <FolderOpen class="icon" aria-hidden="true" />
                打开安装包缓存
              </button>
              <button type="button">
                <ArchiveRestore class="icon" aria-hidden="true" />
                回滚上次安装
              </button>
            </div>
            <p class="muted">{{ statusMessage }}</p>
          </div>

          <div v-if="installPlan" class="panel">
            <h2>文件变更预览</h2>
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
                  <tr v-for="operation in installPlan.Operations" :key="`${operation.Kind}-${operation.DestinationPath}`">
                    <td>{{ operation.Kind }}</td>
                    <td>{{ operation.SourcePath || "-" }}</td>
                    <td>{{ operation.DestinationPath }}</td>
                    <td>{{ operation.Description }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div class="actions">
              <button class="button-primary" type="button">
                <Download class="icon" aria-hidden="true" />
                执行完整安装
              </button>
              <button type="button">
                <ShieldCheck class="icon" aria-hidden="true" />
                查看保护数据
              </button>
            </div>
          </div>
        </section>

        <section v-else-if="activePage === 'config'" class="page">
          <div class="panel">
            <h2>插件配置</h2>
            <div class="field-grid">
              <label class="field">
                <span>目标语言</span>
                <input v-model="configForm.targetLanguage">
              </label>
              <label class="field">
                <span>控制面板端口</span>
                <input v-model.number="configForm.httpPort" type="number">
              </label>
              <label class="field">
                <span>打开面板热键</span>
                <input v-model="configForm.openPanelHotkey">
              </label>
              <label class="field">
                <span>切换翻译热键</span>
                <input v-model="configForm.toggleTranslationHotkey">
              </label>
              <label class="field">
                <span>llama.cpp 模型路径</span>
                <input v-model="configForm.llamaModelPath" placeholder="D:\Models\qwen.gguf">
              </label>
              <label class="field">
                <span>字体替换</span>
                <select v-model="configForm.fontReplacement">
                  <option :value="true">启用</option>
                  <option :value="false">关闭</option>
                </select>
              </label>
            </div>
            <div class="actions">
              <button class="button-primary" type="button">
                <CheckCircle2 class="icon" aria-hidden="true" />
                备份并保存配置
              </button>
              <button type="button">
                <RotateCcw class="icon" aria-hidden="true" />
                重新读取
              </button>
            </div>
          </div>
        </section>

        <section v-else-if="activePage === 'translations'" class="page">
          <div class="panel">
            <h2>译文编辑</h2>
            <div class="stat-grid">
              <div class="stat">
                <span class="muted">翻译缓存</span>
                <strong>18,420</strong>
              </div>
              <div class="stat">
                <span class="muted">术语库</span>
                <strong>312</strong>
              </div>
              <div class="stat">
                <span class="muted">待翻译</span>
                <strong>45</strong>
              </div>
            </div>
            <div class="actions">
              <button class="button-primary" type="button" @click="runMaintenance">
                <Wrench class="icon" aria-hidden="true" />
                备份并重建索引
              </button>
              <button type="button">
                <BookOpen class="icon" aria-hidden="true" />
                导出 CSV
              </button>
            </div>
          </div>

          <div class="panel">
            <h2>最近译文</h2>
            <div class="table-wrap">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>原文</th>
                    <th>译文</th>
                    <th>场景</th>
                    <th>状态</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>Continue</td>
                    <td>继续</td>
                    <td>MainMenu</td>
                    <td>完成</td>
                  </tr>
                  <tr>
                    <td>Options</td>
                    <td>选项</td>
                    <td>MainMenu</td>
                    <td>完成</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-if="maintenanceResult" class="muted">
              最近维护：{{ maintenanceResult.Actions.join("、") }}，备份：{{ maintenanceResult.BackupPath }}
            </p>
          </div>
        </section>

        <section v-else-if="activePage === 'history'" class="page">
          <div class="panel">
            <h2>安装记录</h2>
            <div class="table-wrap">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>时间</th>
                    <th>动作</th>
                    <th>备份目录</th>
                    <th>状态</th>
                  </tr>
                </thead>
                <tbody>
                  <tr>
                    <td>尚无记录</td>
                    <td>等待首次安装</td>
                    <td>-</td>
                    <td>待执行</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </section>

        <section v-else class="page">
          <div class="panel">
            <h2>关于工具箱</h2>
            <div class="target-card">
              <img :src="brandIcon" alt="HUnityAutoTranslator" width="72" height="72">
              <div>
                <strong>HUnityAutoTranslator 外部工具箱</strong>
                <div class="muted">图标、窗口标识和页面品牌均使用项目插件现有 logo 素材。</div>
              </div>
            </div>
          </div>
        </section>
      </main>
    </section>
  </div>
</template>
