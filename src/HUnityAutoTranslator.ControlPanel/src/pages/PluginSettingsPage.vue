<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
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
  ScanIntervalMilliseconds: 750,
  MaxScanTargetsPerTick: 256,
  MaxWritebacksPerFrame: 32,
  MaxSourceTextLength: 2000,
  IgnoreInvisibleText: true,
  SkipNumericSymbolText: true,
  EnableCacheLookup: true,
  EnableTranslationContext: true,
  TranslationContextMaxExamples: 4,
  TranslationContextMaxCharacters: 1200,
  ManualEditsOverrideAi: true,
  ReapplyRememberedTranslations: true,
  CacheRetentionDays: 365,
  EnableFontReplacement: true,
  ReplaceUguiFonts: true,
  ReplaceTmpFonts: true,
  ReplaceImguiFonts: true,
  AutoUseCjkFallbackFonts: true,
  ReplacementFontName: "",
  ReplacementFontFile: "",
  FontSamplingPointSize: 90,
  FontSizeAdjustmentMode: 0,
  FontSizeAdjustmentValue: 0
});

const formDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));
const automaticFontName = computed(() => controlPanelStore.state?.AutomaticReplacementFontName ?? "自动选择");
const automaticFontFile = computed(() => controlPanelStore.state?.AutomaticReplacementFontFile ?? "自动选择");
const listeningHotkeyField = ref<HotkeyField | null>(null);
const isPickingFontFile = ref(false);

function markDirty(): void {
  setDirtyForm(formKey, true);
}

function numberValue(value: number | string): number {
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : 0;
}

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
  form.EnableTranslationContext = state.EnableTranslationContext;
  form.TranslationContextMaxExamples = state.TranslationContextMaxExamples;
  form.TranslationContextMaxCharacters = state.TranslationContextMaxCharacters;
  form.ManualEditsOverrideAi = state.ManualEditsOverrideAi;
  form.ReapplyRememberedTranslations = state.ReapplyRememberedTranslations;
  form.CacheRetentionDays = state.CacheRetentionDays;
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
    EnableTranslationContext: form.EnableTranslationContext,
    TranslationContextMaxExamples: numberValue(form.TranslationContextMaxExamples),
    TranslationContextMaxCharacters: numberValue(form.TranslationContextMaxCharacters),
    ManualEditsOverrideAi: form.ManualEditsOverrideAi,
    ReapplyRememberedTranslations: form.ReapplyRememberedTranslations,
    CacheRetentionDays: numberValue(form.CacheRetentionDays),
    EnableFontReplacement: form.EnableFontReplacement,
    ReplaceUguiFonts: form.ReplaceUguiFonts,
    ReplaceTmpFonts: form.ReplaceTmpFonts,
    ReplaceImguiFonts: form.ReplaceImguiFonts,
    AutoUseCjkFallbackFonts: form.AutoUseCjkFallbackFonts,
    ReplacementFontName: form.ReplacementFontName,
    ReplacementFontFile: form.ReplacementFontFile,
    FontSamplingPointSize: numberValue(form.FontSamplingPointSize),
    FontSizeAdjustmentMode: numberValue(form.FontSizeAdjustmentMode),
    FontSizeAdjustmentValue: numberValue(form.FontSizeAdjustmentValue)
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
        <p>控制采集、写回、热键、缓存策略和字体替换。</p>
      </div>
      <div class="actions">
        <button class="secondary" type="button" :disabled="!formDirty" @click="reset">还原</button>
        <button class="primary" type="button" @click="save">保存插件设置</button>
      </div>
    </div>

    <div class="form-stack" @input="markDirty" @change="markDirty">
      <SectionPanel title="运行与采集">
        <div class="checks">
          <label class="check"><input id="enabled" v-model="form.Enabled" type="checkbox">启用翻译</label>
          <label class="check"><input id="autoOpenControlPanel" v-model="form.AutoOpenControlPanel" type="checkbox">启动后自动打开面板</label>
          <label class="check"><input id="enableUgui" v-model="form.EnableUgui" type="checkbox">采集 UGUI</label>
          <label class="check"><input id="enableTmp" v-model="form.EnableTmp" type="checkbox">采集 TextMeshPro</label>
          <label class="check"><input id="enableImgui" v-model="form.EnableImgui" type="checkbox">采集 IMGUI</label>
        </div>
        <div class="form-grid four">
          <label class="field"><span>扫描间隔 (毫秒)</span><input id="scanIntervalMilliseconds" v-model.number="form.ScanIntervalMilliseconds" type="number" min="100"></label>
          <label class="field"><span>每次扫描上限</span><input id="maxScanTargetsPerTick" v-model.number="form.MaxScanTargetsPerTick" type="number" min="1"></label>
          <label class="field"><span>每帧写回上限</span><input id="maxWritebacksPerFrame" v-model.number="form.MaxWritebacksPerFrame" type="number" min="1"></label>
          <label class="field"><span>原文长度上限</span><input id="maxSourceTextLength" v-model.number="form.MaxSourceTextLength" type="number" min="1"></label>
        </div>
      </SectionPanel>

      <SectionPanel title="文本策略">
        <div class="checks">
          <label class="check"><input id="ignoreInvisibleText" v-model="form.IgnoreInvisibleText" type="checkbox">忽略不可见文本</label>
          <label class="check"><input id="skipNumericSymbolText" v-model="form.SkipNumericSymbolText" type="checkbox">跳过数字/符号文本</label>
          <label class="check"><input id="enableCacheLookup" v-model="form.EnableCacheLookup" type="checkbox">启用缓存查找</label>
          <label class="check"><input id="enableTranslationContext" v-model="form.EnableTranslationContext" type="checkbox">启用翻译上下文</label>
          <label class="check"><input id="manualEditsOverrideAi" v-model="form.ManualEditsOverrideAi" type="checkbox">手动编辑优先</label>
          <label class="check"><input id="reapplyRememberedTranslations" v-model="form.ReapplyRememberedTranslations" type="checkbox">重新应用已记住译文</label>
        </div>
        <div class="form-grid three">
          <label class="field"><span>上下文示例数</span><input id="translationContextMaxExamples" v-model.number="form.TranslationContextMaxExamples" type="number" min="0"></label>
          <label class="field"><span>上下文字符数</span><input id="translationContextMaxCharacters" v-model.number="form.TranslationContextMaxCharacters" type="number" min="0"></label>
          <label class="field"><span>缓存保留天数</span><input id="cacheRetentionDays" v-model.number="form.CacheRetentionDays" type="number" min="1"></label>
        </div>
      </SectionPanel>

      <SectionPanel title="快捷键">
        <div class="form-grid four">
          <label class="field"><span>打开控制面板</span><input id="openControlPanelHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'OpenControlPanelHotkey' }" :value="hotkeyValue('OpenControlPanelHotkey')" readonly autocomplete="off" placeholder="Alt+H" @focus="beginHotkeyCapture('OpenControlPanelHotkey')" @click="beginHotkeyCapture('OpenControlPanelHotkey')" @blur="cancelHotkeyCapture('OpenControlPanelHotkey')" @keydown="handleHotkeyKeydown($event, 'OpenControlPanelHotkey')"></label>
          <label class="field"><span>原文/译文切换</span><input id="toggleTranslationHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleTranslationHotkey' }" :value="hotkeyValue('ToggleTranslationHotkey')" readonly autocomplete="off" placeholder="Alt+F" @focus="beginHotkeyCapture('ToggleTranslationHotkey')" @click="beginHotkeyCapture('ToggleTranslationHotkey')" @blur="cancelHotkeyCapture('ToggleTranslationHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleTranslationHotkey')"></label>
          <label class="field"><span>全局扫描更新</span><input id="forceScanHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ForceScanHotkey' }" :value="hotkeyValue('ForceScanHotkey')" readonly autocomplete="off" placeholder="Alt+G" @focus="beginHotkeyCapture('ForceScanHotkey')" @click="beginHotkeyCapture('ForceScanHotkey')" @blur="cancelHotkeyCapture('ForceScanHotkey')" @keydown="handleHotkeyKeydown($event, 'ForceScanHotkey')"></label>
          <label class="field"><span>字体状态切换</span><input id="toggleFontHotkey" class="hotkey-input" :class="{ listening: listeningHotkeyField === 'ToggleFontHotkey' }" :value="hotkeyValue('ToggleFontHotkey')" readonly autocomplete="off" placeholder="Alt+D" @focus="beginHotkeyCapture('ToggleFontHotkey')" @click="beginHotkeyCapture('ToggleFontHotkey')" @blur="cancelHotkeyCapture('ToggleFontHotkey')" @keydown="handleHotkeyKeydown($event, 'ToggleFontHotkey')"></label>
        </div>
      </SectionPanel>

      <SectionPanel title="字体替换">
        <div class="checks">
          <label class="check"><input id="enableFontReplacement" v-model="form.EnableFontReplacement" type="checkbox">启用字体替换</label>
          <label class="check"><input id="replaceUguiFonts" v-model="form.ReplaceUguiFonts" type="checkbox">UGUI 替换字体</label>
          <label class="check"><input id="replaceTmpFonts" v-model="form.ReplaceTmpFonts" type="checkbox">TextMeshPro fallback</label>
          <label class="check"><input id="replaceImguiFonts" v-model="form.ReplaceImguiFonts" type="checkbox">IMGUI 替换字体</label>
          <label class="check"><input id="autoUseCjkFallbackFonts" v-model="form.AutoUseCjkFallbackFonts" type="checkbox">自动使用 CJK 字体</label>
        </div>
        <div class="form-grid two">
          <label class="field">
            <span>手动字体名</span>
            <input id="replacementFontName" v-model="form.ReplacementFontName" autocomplete="off" :placeholder="`留空自动选择：${automaticFontName}`">
          </label>
          <div class="field">
            <span>手动字体文件</span>
            <div class="input-action-row">
              <input id="replacementFontFile" v-model="form.ReplacementFontFile" autocomplete="off" :placeholder="`留空自动选择 TTF：${automaticFontFile}`">
              <button id="pickReplacementFontFile" class="secondary" type="button" :disabled="isPickingFontFile" @click="pickReplacementFontFile">
                {{ isPickingFontFile ? "选择中..." : "选择字体文件" }}
              </button>
            </div>
          </div>
        </div>
        <div class="form-grid three">
          <label class="field"><span>字体采样字号</span><input id="fontSamplingPointSize" v-model.number="form.FontSamplingPointSize" type="number" min="16"></label>
          <label class="field"><span>字号调整模式</span>
            <select id="fontSizeAdjustmentMode" v-model.number="form.FontSizeAdjustmentMode">
              <option :value="0">关闭</option>
              <option :value="1">按比例</option>
              <option :value="2">固定增减</option>
            </select>
          </label>
          <label class="field"><span>字号调整值</span><input id="fontSizeAdjustmentValue" v-model.number="form.FontSizeAdjustmentValue" type="number" step="0.1"></label>
        </div>
      </SectionPanel>
    </div>
  </section>
</template>
