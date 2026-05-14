<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import {
  FileText,
  Keyboard,
  Maximize2,
  Pencil,
  RotateCcw,
  Save,
  ScanLine,
  Timer,
  Type,
  Wand2
} from "lucide-vue-next";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, pickFontFile, saveConfig, setDirtyForm, showToast } from "../state/controlPanelStore";
import type { ControlPanelState, UpdateConfigRequest } from "../types/api";

const formKey = "plugin";
type HotkeyField = "OpenControlPanelHotkey" | "ToggleTranslationHotkey" | "ForceScanHotkey" | "ToggleFontHotkey";
const hotkeyListeningText = "请按组合键...";

const form = reactive({
  Enabled: true,
  AutoOpenControlPanel: true,
  OpenControlPanelHotkey: "Alt+H",
  ToggleTranslationHotkey: "Alt+F",
  ForceScanHotkey: "Alt+G",
  ToggleFontHotkey: "Alt+D",
  EnableUgui: true,
  EnableTmp: true,
  EnableImgui: true,
  ScanIntervalMilliseconds: 100,
  MaxScanTargetsPerTick: 256,
  MaxWritebacksPerFrame: 32,
  MaxSourceTextLength: 2000,
  IgnoreInvisibleText: true,
  SkipNumericSymbolText: true,
  EnableCacheLookup: true,
  EnableTranslationDebugLogs: false,
  ManualEditsOverrideAi: true,
  ReapplyRememberedTranslations: true,
  EnableFontReplacement: true,
  ReplaceUguiFonts: true,
  ReplaceTmpFonts: true,
  ReplaceImguiFonts: true,
  AutoUseCjkFallbackFonts: true,
  ReplacementFontName: "",
  ReplacementFontFile: "",
  FontSamplingPointSize: 90,
  FontSizeAdjustmentMode: 0,
  FontSizeAdjustmentValue: 0,
  EnableTmpNativeAutoSize: false
});

const formDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));
const automaticFontSummary = computed(() => {
  const name = controlPanelStore.state?.AutomaticReplacementFontName?.trim() || "";
  const file = controlPanelStore.state?.AutomaticReplacementFontFile?.trim() || "";
  return {
    name: name || "尚未检测到自动字体",
    file: file || "尚未检测到自动字体文件"
  };
});
const automaticFontName = computed(() => automaticFontSummary.value.name);
const automaticFontFile = computed(() => automaticFontSummary.value.file);
const listeningHotkeyField = ref<HotkeyField | null>(null);
const isPickingFontFile = ref(false);

function markDirty(): void {
  setDirtyForm(formKey, true);
}

function numberValue(value: number | string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

const fontSizeAdjustmentEnabled = computed({
  get: () => numberValue(form.FontSizeAdjustmentMode) !== 0,
  set: (enabled: boolean) => {
    const currentMode = numberValue(form.FontSizeAdjustmentMode);
    form.FontSizeAdjustmentMode = enabled
      ? (currentMode === 0 ? 1 : currentMode)
      : 0;
  }
});

function beginHotkeyCapture(field: HotkeyField): void {
  listeningHotkeyField.value = field;
}

function cancelHotkeyCapture(field: HotkeyField): void {
  if (listeningHotkeyField.value === field) {
    listeningHotkeyField.value = null;
  }
}

function hotkeyValue(field: HotkeyField): string {
  return listeningHotkeyField.value === field ? hotkeyListeningText : form[field];
}

function isModifierKey(key: string): boolean {
  return key === "Control" || key === "Shift" || key === "Alt" || key === "Meta";
}

function normalizeCapturedKey(key: string): string | null {
  if (/^[a-z]$/i.test(key)) {
    return key.toUpperCase();
  }

  if (/^[0-9]$/.test(key)) {
    return key;
  }

  if (/^F([1-9]|1[0-2])$/i.test(key)) {
    return key.toUpperCase();
  }

  const knownKeys: Record<string, string> = {
    " ": "Space",
    Spacebar: "Space",
    Space: "Space",
    Enter: "Enter",
    Tab: "Tab",
    Backspace: "Backspace",
    Escape: "Escape",
    Esc: "Escape",
    Insert: "Insert",
    Delete: "Delete",
    Home: "Home",
    End: "End",
    PageUp: "PageUp",
    PageDown: "PageDown",
    ArrowUp: "UpArrow",
    ArrowDown: "DownArrow",
    ArrowLeft: "LeftArrow",
    ArrowRight: "RightArrow"
  };

  return knownKeys[key] ?? null;
}

function normalizeCapturedHotkey(event: KeyboardEvent): string | null {
  const key = normalizeCapturedKey(event.key);
  if (!key) {
    return null;
  }

  const parts: string[] = [];
  if (event.ctrlKey) {
    parts.push("Ctrl");
  }
  if (event.shiftKey) {
    parts.push("Shift");
  }
  if (event.altKey) {
    parts.push("Alt");
  }
  if (!parts.length) {
    return null;
  }

  parts.push(key);
  return parts.join("+");
}

function handleHotkeyKeydown(event: KeyboardEvent, field: HotkeyField): void {
  if (listeningHotkeyField.value !== field) {
    return;
  }

  event.preventDefault();
  event.stopPropagation();

  if (event.key === "Escape") {
    cancelHotkeyCapture(field);
    return;
  }

  if (isModifierKey(event.key)) {
    return;
  }

  if ((event.key === "Backspace" || event.key === "Delete") && !event.ctrlKey && !event.shiftKey && !event.altKey) {
    form[field] = "None";
    listeningHotkeyField.value = null;
    markDirty();
    return;
  }

  if (!normalizeCapturedKey(event.key)) {
    showToast("不支持这个按键，请换一个主键。", "warn");
    return;
  }

  const hotkey = normalizeCapturedHotkey(event);
  if (!hotkey) {
    showToast("需要使用 Ctrl、Shift 或 Alt 组合键。", "warn");
    return;
  }

  form[field] = hotkey;
  listeningHotkeyField.value = null;
  markDirty();
}

function applyState(state: ControlPanelState | null, force = false): void {
  if (!state || (!force && formDirty.value)) {
    return;
  }

  form.Enabled = state.Enabled;
  form.AutoOpenControlPanel = state.AutoOpenControlPanel;
  form.OpenControlPanelHotkey = state.OpenControlPanelHotkey;
  form.ToggleTranslationHotkey = state.ToggleTranslationHotkey;
  form.ForceScanHotkey = state.ForceScanHotkey;
  form.ToggleFontHotkey = state.ToggleFontHotkey;
  form.EnableUgui = state.EnableUgui;
  form.EnableTmp = state.EnableTmp;
  form.EnableImgui = state.EnableImgui;
  form.ScanIntervalMilliseconds = state.ScanIntervalMilliseconds;
  form.MaxScanTargetsPerTick = state.MaxScanTargetsPerTick;
  form.MaxWritebacksPerFrame = state.MaxWritebacksPerFrame;
  form.MaxSourceTextLength = state.MaxSourceTextLength;
  form.IgnoreInvisibleText = state.IgnoreInvisibleText;
  form.SkipNumericSymbolText = state.SkipNumericSymbolText;
  form.EnableCacheLookup = state.EnableCacheLookup;
  form.EnableTranslationDebugLogs = state.EnableTranslationDebugLogs;
  form.ManualEditsOverrideAi = state.ManualEditsOverrideAi;
  form.ReapplyRememberedTranslations = state.ReapplyRememberedTranslations;
  form.EnableFontReplacement = state.EnableFontReplacement;
  form.ReplaceUguiFonts = state.ReplaceUguiFonts;
  form.ReplaceTmpFonts = state.ReplaceTmpFonts;
  form.ReplaceImguiFonts = state.ReplaceImguiFonts;
  form.AutoUseCjkFallbackFonts = state.AutoUseCjkFallbackFonts;
  form.ReplacementFontName = state.ReplacementFontName ?? "";
  form.ReplacementFontFile = state.ReplacementFontFile ?? "";
  form.FontSamplingPointSize = state.FontSamplingPointSize;
  form.FontSizeAdjustmentMode = numberValue(state.FontSizeAdjustmentMode);
  form.FontSizeAdjustmentValue = state.FontSizeAdjustmentValue;
  form.EnableTmpNativeAutoSize = state.EnableTmpNativeAutoSize;
  setDirtyForm(formKey, false);
}

function readConfig(): UpdateConfigRequest {
  return {
    Enabled: form.Enabled,
    AutoOpenControlPanel: form.AutoOpenControlPanel,
    OpenControlPanelHotkey: form.OpenControlPanelHotkey,
    ToggleTranslationHotkey: form.ToggleTranslationHotkey,
    ForceScanHotkey: form.ForceScanHotkey,
    ToggleFontHotkey: form.ToggleFontHotkey,
    EnableUgui: form.EnableUgui,
    EnableTmp: form.EnableTmp,
    EnableImgui: form.EnableImgui,
    ScanIntervalMilliseconds: numberValue(form.ScanIntervalMilliseconds),
    MaxScanTargetsPerTick: numberValue(form.MaxScanTargetsPerTick),
    MaxWritebacksPerFrame: numberValue(form.MaxWritebacksPerFrame),
    MaxSourceTextLength: numberValue(form.MaxSourceTextLength),
    IgnoreInvisibleText: form.IgnoreInvisibleText,
    SkipNumericSymbolText: form.SkipNumericSymbolText,
    EnableCacheLookup: form.EnableCacheLookup,
    EnableTranslationDebugLogs: form.EnableTranslationDebugLogs,
    ManualEditsOverrideAi: form.ManualEditsOverrideAi,
    ReapplyRememberedTranslations: form.ReapplyRememberedTranslations,
    EnableFontReplacement: form.EnableFontReplacement,
    ReplaceUguiFonts: form.ReplaceUguiFonts,
    ReplaceTmpFonts: form.ReplaceTmpFonts,
    ReplaceImguiFonts: form.ReplaceImguiFonts,
    AutoUseCjkFallbackFonts: form.AutoUseCjkFallbackFonts,
    ReplacementFontName: form.ReplacementFontName,
    ReplacementFontFile: form.ReplacementFontFile,
    FontSamplingPointSize: numberValue(form.FontSamplingPointSize),
    FontSizeAdjustmentMode: numberValue(form.FontSizeAdjustmentMode),
    FontSizeAdjustmentValue: numberValue(form.FontSizeAdjustmentValue),
    EnableTmpNativeAutoSize: form.EnableTmpNativeAutoSize
  };
}

async function save(): Promise<void> {
  const state = await saveConfig(readConfig(), formKey);
  applyState(state, true);
}

async function pickReplacementFontFile(): Promise<void> {
  if (isPickingFontFile.value) {
    return;
  }

  isPickingFontFile.value = true;
  try {
    const result = await pickFontFile();
    if (result.Status === "selected" && result.FilePath) {
      form.ReplacementFontFile = result.FilePath;
      form.ReplacementFontName = result.FontName ?? "";
      markDirty();
      showToast(result.FontName ? `已选择字体：${result.FontName}` : "已选择字体文件", "ok");
      return;
    }

    if (result.Status !== "cancelled") {
      showToast(result.Message || "选择字体文件失败。", result.Status === "unsupported" ? "warn" : "error");
    }
  } catch (error) {
    showToast(error instanceof Error ? error.message : "选择字体文件失败。", "error");
  } finally {
    isPickingFontFile.value = false;
  }
}

function reset(): void {
  applyState(controlPanelStore.state, true);
}

watch(() => controlPanelStore.state, (state) => applyState(state), { immediate: true });
</script>

<template>
  <section class="page active" id="page-plugin">
    <div class="page-head">
      <div>
        <h1>插件设置</h1>
        <p>控制采集、写回、热键、缓存策略和字体补字 fallback。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="!formDirty" @click="reset"><RotateCcw class="button-icon" />还原</button>
        <button class="primary" type="button" @click="save"><Save class="button-icon" />保存插件设置</button>
      </div>
    </div>

    <div class="form-stack" @input="markDirty" @change="markDirty">
      <SectionPanel title="运行与采集" :icon="ScanLine">
        <div class="checks">
          <label class="check help-target" data-help="暂停采集、翻译和写回，已保存的配置和缓存不会被删除。"><input id="enabled" v-model="form.Enabled" type="checkbox">启用翻译</label>
          <label class="check help-target" data-help="插件加载后自动在浏览器打开本机控制面板，关闭后仍可用快捷键打开。"><input id="autoOpenControlPanel" v-model="form.AutoOpenControlPanel" type="checkbox">启动后自动打开面板</label>
          <label class="check help-target" data-help="采集 Unity UGUI Text 文本，常见于菜单、按钮和传统界面。"><input id="enableUgui" v-model="form.EnableUgui" type="checkbox">采集 UGUI</label>
          <label class="check help-target" data-help="采集 TextMeshPro 文本，常见于较新的 Unity UI 和高质量字体渲染。"><input id="enableTmp" v-model="form.EnableTmp" type="checkbox">采集 TextMeshPro</label>
          <label class="check help-target" data-help="采集 IMGUI 绘制的文本，通常用于调试窗口或旧式插件界面。"><input id="enableImgui" v-model="form.EnableImgui" type="checkbox">采集 IMGUI</label>
        </div>
        <div class="form-grid four">
          <label class="field range-field help-target" data-help="控制插件多久扫描一次 Unity 文本，数值越小越及时但更耗性能。">
            <span class="field-label"><Timer class="field-label-icon" />扫描间隔 (毫秒)</span>
            <span class="range-row">
              <input v-model.number="form.ScanIntervalMilliseconds" type="range" min="20" max="1000" step="10">
              <input id="scanIntervalMilliseconds" v-model.number="form.ScanIntervalMilliseconds" class="range-value" type="number" min="20" max="1000">
            </span>
          </label>
          <label class="field range-field help-target" data-help="限制单次扫描最多处理多少个文本目标，降低它可减少单次卡顿。">
            <span class="field-label"><ScanLine class="field-label-icon" />每次扫描上限</span>
            <span class="range-row">
              <input v-model.number="form.MaxScanTargetsPerTick" type="range" min="16" max="2048" step="16">
              <input id="maxScanTargetsPerTick" v-model.number="form.MaxScanTargetsPerTick" class="range-value" type="number" min="16" max="2048">
            </span>
          </label>
          <label class="field range-field help-target" data-help="限制每帧最多把多少条译文写回游戏界面，降低它可减少瞬时压力。">
            <span class="field-label"><Pencil class="field-label-icon" />每帧写回上限</span>
            <span class="range-row">
              <input v-model.number="form.MaxWritebacksPerFrame" type="range" min="4" max="256" step="4">
              <input id="maxWritebacksPerFrame" v-model.number="form.MaxWritebacksPerFrame" class="range-value" type="number" min="4" max="256">
            </span>
          </label>
          <label class="field range-field help-target" data-help="超过这个长度的原文不会进入翻译队列，用来跳过异常长文本或日志。">
            <span class="field-label"><Maximize2 class="field-label-icon" />原文长度上限</span>
            <span class="range-row">
              <input v-model.number="form.MaxSourceTextLength" type="range" min="100" max="8000" step="100">
              <input id="maxSourceTextLength" v-model.number="form.MaxSourceTextLength" class="range-value" type="number" min="100" max="8000">
            </span>
          </label>
        </div>
      </SectionPanel>

      <SectionPanel title="文本策略" :icon="FileText">
        <div class="checks">
          <label class="check help-target" data-help="跳过当前不可见的 UI 文本，减少隐藏界面和模板文本进入翻译队列。"><input id="ignoreInvisibleText" v-model="form.IgnoreInvisibleText" type="checkbox">忽略不可见文本</label>
          <label class="check help-target" data-help="跳过只有数字、标点或符号的文本，避免无意义请求和错误替换。"><input id="skipNumericSymbolText" v-model="form.SkipNumericSymbolText" type="checkbox">跳过数字/符号文本</label>
          <label class="check help-target" data-help="优先从本地缓存读取已有译文，命中后不会再次请求 AI。"><input id="enableCacheLookup" v-model="form.EnableCacheLookup" type="checkbox">启用缓存查找</label>
          <label class="check help-target" data-help="在 BepInEx 日志输出请求和响应细节，排查提示词或模型问题时再开启。"><input id="enableTranslationDebugLogs" v-model="form.EnableTranslationDebugLogs" type="checkbox">输出翻译请求调试日志</label>
          <label class="check help-target" data-help="控制面板里手动改过的译文优先于 AI 结果，避免被后续自动翻译覆盖。"><input id="manualEditsOverrideAi" v-model="form.ManualEditsOverrideAi" type="checkbox">手动编辑优先</label>
          <label class="check help-target" data-help="重新扫描时把缓存中记住的译文再次写回游戏，适合界面重建后恢复翻译。"><input id="reapplyRememberedTranslations" v-model="form.ReapplyRememberedTranslations" type="checkbox">重新应用已记住译文</label>
        </div>
      </SectionPanel>

      <SectionPanel title="快捷键" :icon="Keyboard">
        <div class="form-grid four">
          <label class="field help-target" data-help="点击后直接监听组合键，按 Backspace 或 Delete 可清空为 None。"><span class="field-label"><Keyboard class="field-label-icon" />打开控制面板</span><input id="openControlPanelHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'OpenControlPanelHotkey' }" :value="hotkeyValue('OpenControlPanelHotkey')" readonly autocomplete="off" placeholder="Alt+H" @focus="beginHotkeyCapture('OpenControlPanelHotkey')" @click="beginHotkeyCapture('OpenControlPanelHotkey')" @blur="cancelHotkeyCapture('OpenControlPanelHotkey')" @keydown="handleHotkeyKeydown($event, 'OpenControlPanelHotkey')"></label>
          <label class="field help-target" data-help="在游戏中临时切换显示原文或译文，不会改写缓存内容。"><span class="field-label"><Wand2 class="field-label-icon" />原文/译文切换</span><input id="toggleTranslationHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleTranslationHotkey' }" :value="hotkeyValue('ToggleTranslationHotkey')" readonly autocomplete="off" placeholder="Alt+F" @focus="beginHotkeyCapture('ToggleTranslationHotkey')" @click="beginHotkeyCapture('ToggleTranslationHotkey')" @blur="cancelHotkeyCapture('ToggleTranslationHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleTranslationHotkey')"></label>
          <label class="field help-target" data-help="立即重新扫描当前场景文本，适合界面变化后手动刷新目标。"><span class="field-label"><ScanLine class="field-label-icon" />全局扫描更新</span><input id="forceScanHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ForceScanHotkey' }" :value="hotkeyValue('ForceScanHotkey')" readonly autocomplete="off" placeholder="Alt+G" @focus="beginHotkeyCapture('ForceScanHotkey')" @click="beginHotkeyCapture('ForceScanHotkey')" @blur="cancelHotkeyCapture('ForceScanHotkey')" @keydown="handleHotkeyKeydown($event, 'ForceScanHotkey')"></label>
          <label class="field help-target" data-help="在游戏中临时启用或恢复字体补字 fallback；TMP 会优先保留原字体材质和效果。"><span class="field-label"><Type class="field-label-icon" />字体状态切换</span><input id="toggleFontHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleFontHotkey' }" :value="hotkeyValue('ToggleFontHotkey')" readonly autocomplete="off" placeholder="Alt+D" @focus="beginHotkeyCapture('ToggleFontHotkey')" @click="beginHotkeyCapture('ToggleFontHotkey')" @blur="cancelHotkeyCapture('ToggleFontHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleFontHotkey')"></label>
        </div>
      </SectionPanel>

      <SectionPanel title="字体补字 fallback" :icon="Type">
        <div class="checks font-replacement-checks">
          <label class="check font-primary-toggle help-target" data-help="优先保留原字体，只在译文缺字时补充中文 fallback。"><input id="enableFontReplacement" v-model="form.EnableFontReplacement" type="checkbox">启用字体补字 fallback</label>
          <label class="check help-target" data-help="UGUI Text 缺字时才切换到中文字体，减少方块字风险。"><input id="replaceUguiFonts" v-model="form.ReplaceUguiFonts" type="checkbox">UGUI 缺字补字</label>
          <label class="check help-target" data-help="为 TextMeshPro 缺字文本安装 fallback 字体，优先保留原 TMP 字体资产和材质。"><input id="replaceTmpFonts" v-model="form.ReplaceTmpFonts" type="checkbox">TextMeshPro fallback</label>
          <label class="check help-target" data-help="IMGUI 当前绘制项缺字时才临时补充中文字体，绘制后立即恢复原皮肤字体。"><input id="replaceImguiFonts" v-model="form.ReplaceImguiFonts" type="checkbox">IMGUI 临时补字</label>
          <label class="check help-target" data-help="未手动指定字体时自动选择系统 CJK 字体，适合作为默认兜底。"><input id="autoUseCjkFallbackFonts" v-model="form.AutoUseCjkFallbackFonts" type="checkbox">自动使用 CJK 字体</label>
        </div>
        <div id="automaticReplacementFontSummary" class="automatic-font-summary">
          <div>
            <span>自动字体名</span>
            <strong>{{ automaticFontSummary.name }}</strong>
          </div>
          <div>
            <span>自动字体文件</span>
            <strong>{{ automaticFontSummary.file }}</strong>
          </div>
        </div>
        <div class="form-grid two">
          <label class="field help-target" data-help="指定字体 family 名称；留空时使用自动检测到的中文字体。">
            <span class="field-label"><Type class="field-label-icon" />手动字体名</span>
            <input id="replacementFontName" v-model="form.ReplacementFontName" autocomplete="off" :placeholder="`留空自动选择：${automaticFontName}`">
          </label>
          <div class="field help-target" data-help="指定 TTF/OTF 字体文件路径；选择文件会自动填入字体名，保存后生效。">
            <span class="field-label"><FileText class="field-label-icon" />手动字体文件</span>
            <div class="input-action-row">
              <input id="replacementFontFile" v-model="form.ReplacementFontFile" autocomplete="off" :placeholder="`留空自动选择 TTF：${automaticFontFile}`">
              <button id="pickReplacementFontFile" class="secondary" type="button" :disabled="isPickingFontFile" @click="pickReplacementFontFile">
                <FileText class="button-icon" />
                {{ isPickingFontFile ? "选择中..." : "选择字体文件" }}
              </button>
            </div>
          </div>
        </div>
        <div class="font-size-settings">
          <label class="field help-target" data-help="创建 TMP fallback 字体时使用的采样字号，较大值可提升字形质量但更耗内存。"><span class="field-label"><Type class="field-label-icon" />字体采样字号</span><input id="fontSamplingPointSize" v-model.number="form.FontSamplingPointSize" type="number" min="16"></label>
          <label class="check font-size-adjustment-toggle help-target" data-help="按原文本组件大小调整译文字号，关闭时不显示调整值。">
            <input id="fontSizeAdjustmentEnabled" v-model="fontSizeAdjustmentEnabled" type="checkbox">
            <Wand2 class="field-label-icon" />
            启用字号调整
          </label>
          <label class="check font-size-adjustment-toggle help-target" data-help="默认关闭；使用 TextMeshPro 自带 Auto Size，最大不超过原字号，最小为原字号的 75%。">
            <input id="enableTmpNativeAutoSize" v-model="form.EnableTmpNativeAutoSize" type="checkbox">
            <Maximize2 class="field-label-icon" />
            TMP 原生字号适配
          </label>
          <label v-if="fontSizeAdjustmentEnabled" class="field font-size-value-field help-target" data-help="按比例时填百分比增减；固定增减时填字号点数增减，负数会缩小译文。">
            <span class="field-label"><Maximize2 class="field-label-icon" />字号调整值</span>
            <input id="fontSizeAdjustmentValue" v-model.number="form.FontSizeAdjustmentValue" type="number" step="0.1">
          </label>
          <div v-if="fontSizeAdjustmentEnabled" class="field font-size-mode-field help-target" data-help="选择字号调整值按百分比例计算，还是按具体字号点数增减。">
            <span class="field-label"><Wand2 class="field-label-icon" />调整方式</span>
            <div class="segmented-control font-size-mode-control" role="radiogroup" aria-label="字号调整方式">
              <label>
                <input id="fontSizeAdjustmentModePercent" v-model.number="form.FontSizeAdjustmentMode" type="radio" :value="1">
                <span>百分比例</span>
              </label>
              <label>
                <input id="fontSizeAdjustmentModePoints" v-model.number="form.FontSizeAdjustmentMode" type="radio" :value="2">
                <span>固定增减</span>
              </label>
            </div>
          </div>
        </div>
      </SectionPanel>
    </div>
  </section>
</template>
