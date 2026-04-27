<script setup lang="ts">
import { computed, onMounted, reactive, ref, watch } from "vue";
import { buildQuery, deleteJson, getJson, patchJson, postJson } from "../api/client";
import SectionPanel from "../components/SectionPanel.vue";
import { controlPanelStore, saveConfig, setDirtyForm, showToast } from "../state/controlPanelStore";
import type { DeleteResult, GlossaryTerm, GlossaryTermPage, GlossaryTermRequest, UpdateConfigRequest } from "../types/api";
import { formatDateTime } from "../utils/format";

const formKey = "glossary-settings";
const rows = ref<GlossaryTerm[]>([]);
const totalCount = ref(0);
const search = ref("");
const loading = ref(false);

const settings = reactive({
  EnableGlossary: true,
  EnableAutoTermExtraction: false,
  GlossaryMaxTerms: 16,
  GlossaryMaxCharacters: 1200
});

const inlineTerm = reactive({
  SourceTerm: "",
  TargetTerm: "",
  TargetLanguage: "zh-Hans",
  Note: "",
  Enabled: true
});

const settingsDirty = computed(() => controlPanelStore.dirtyForms.has(formKey));

function markSettingsDirty(): void {
  setDirtyForm(formKey, true);
}

function applyState(): void {
  const state = controlPanelStore.state;
  if (!state || settingsDirty.value) {
    return;
  }

  settings.EnableGlossary = state.EnableGlossary;
  settings.EnableAutoTermExtraction = state.EnableAutoTermExtraction;
  settings.GlossaryMaxTerms = state.GlossaryMaxTerms;
  settings.GlossaryMaxCharacters = state.GlossaryMaxCharacters;
  if (!inlineTerm.TargetLanguage) {
    inlineTerm.TargetLanguage = state.TargetLanguage || "zh-Hans";
  }
}

function readSettings(): UpdateConfigRequest {
  return {
    EnableGlossary: settings.EnableGlossary,
    EnableAutoTermExtraction: settings.EnableAutoTermExtraction,
    GlossaryMaxTerms: Number(settings.GlossaryMaxTerms),
    GlossaryMaxCharacters: Number(settings.GlossaryMaxCharacters)
  };
}

async function saveGlossarySettings(): Promise<void> {
  await saveConfig(readSettings(), formKey);
}

async function loadGlossaryTerms(): Promise<void> {
  loading.value = true;
  try {
    const page = await getJson<GlossaryTermPage>(buildQuery("/api/glossary", {
      search: search.value,
      sort: "updated_utc",
      direction: "desc",
      offset: 0,
      limit: 100
    }));
    rows.value = page.Items;
    totalCount.value = page.TotalCount;
  } catch (error) {
    showToast(error instanceof Error ? error.message : "术语加载失败", "error");
  } finally {
    loading.value = false;
  }
}

function readInlineTerm(): GlossaryTermRequest {
  return {
    SourceTerm: inlineTerm.SourceTerm.trim(),
    TargetTerm: inlineTerm.TargetTerm.trim(),
    TargetLanguage: inlineTerm.TargetLanguage.trim() || controlPanelStore.state?.TargetLanguage || "zh-Hans",
    Note: inlineTerm.Note.trim() || null,
    Enabled: inlineTerm.Enabled
  };
}

function resetInlineTerm(): void {
  inlineTerm.SourceTerm = "";
  inlineTerm.TargetTerm = "";
  inlineTerm.TargetLanguage = controlPanelStore.state?.TargetLanguage || "zh-Hans";
  inlineTerm.Note = "";
  inlineTerm.Enabled = true;
}

function editGlossaryTerm(row: GlossaryTerm): void {
  inlineTerm.SourceTerm = row.SourceTerm;
  inlineTerm.TargetTerm = row.TargetTerm;
  inlineTerm.TargetLanguage = row.TargetLanguage;
  inlineTerm.Note = row.Note ?? "";
  inlineTerm.Enabled = row.Enabled;
}

async function saveGlossaryTerm(term?: GlossaryTermRequest): Promise<void> {
  const payload = term ?? readInlineTerm();
  if (!payload.SourceTerm || !payload.TargetTerm) {
    showToast("请填写原术语和指定译名。", "warn");
    return;
  }

  const page = term
    ? await patchJson<GlossaryTermPage>("/api/glossary", payload)
    : await postJson<GlossaryTermPage>("/api/glossary", payload);
  rows.value = page.Items;
  totalCount.value = page.TotalCount;
  resetInlineTerm();
  showToast("术语已保存。", "ok");
}

async function toggleGlossaryTerm(row: GlossaryTerm, enabled: boolean): Promise<void> {
  await saveGlossaryTerm({
    SourceTerm: row.SourceTerm,
    TargetTerm: row.TargetTerm,
    TargetLanguage: row.TargetLanguage,
    Note: row.Note,
    UsageCount: row.UsageCount,
    Enabled: enabled
  });
}

async function deleteGlossaryTerm(row: GlossaryTerm): Promise<void> {
  if (!confirm(`删除术语“${row.SourceTerm}”？`)) {
    return;
  }

  const result = await deleteJson<DeleteResult>("/api/glossary", [row]);
  await loadGlossaryTerms();
  showToast(`已删除 ${result.DeletedCount || 1} 条术语。`, "ok");
}

watch(() => controlPanelStore.state, applyState, { immediate: true });
watch(search, () => {
  void loadGlossaryTerms();
});
onMounted(loadGlossaryTerms);
</script>

<template>
  <section class="page active" id="page-glossary" data-page="glossary">
    <div class="page-head">
      <div>
        <h1>术语库</h1>
        <p>维护固定译名，并控制 AI 自动术语提取。</p>
      </div>
      <button class="primary" id="saveGlossarySettings" type="button" @click="saveGlossarySettings">保存术语设置</button>
    </div>

    <div class="form-stack">
      <SectionPanel title="术语设置">
        <div class="form-grid four" @input="markSettingsDirty" @change="markSettingsDirty">
          <label class="field"><span>注入术语上限</span><input id="glossaryMaxTerms" v-model.number="settings.GlossaryMaxTerms" type="number" min="0" max="100"></label>
          <label class="field"><span>术语字符上限</span><input id="glossaryMaxCharacters" v-model.number="settings.GlossaryMaxCharacters" type="number" min="0" max="8000"></label>
        </div>
        <div class="checks" @change="markSettingsDirty">
          <label class="check"><input id="enableGlossary" v-model="settings.EnableGlossary" type="checkbox">启用术语库约束</label>
          <label class="check"><input id="enableAutoTermExtraction" v-model="settings.EnableAutoTermExtraction" type="checkbox">启用 AI 自动提取术语</label>
        </div>
        <p class="hint">AI 自动提取默认关闭；手动术语会优先进入提示词约束。</p>
      </SectionPanel>

      <SectionPanel title="术语条目">
        <template #actions>
          <button id="refreshGlossary" class="secondary" type="button" :disabled="loading" @click="loadGlossaryTerms">
            {{ loading ? "刷新中" : "刷新" }}
          </button>
        </template>
        <div class="editor-tools">
          <input id="glossarySearch" v-model="search" placeholder="搜索原术语、指定译名、语言或备注">
        </div>
        <div class="table-wrap" id="glossaryWrap" tabindex="0">
          <table>
            <thead>
              <tr>
                <th>启用</th>
                <th>原术语</th>
                <th>指定译名</th>
                <th>目标语言</th>
                <th>来源</th>
                <th>使用</th>
                <th>更新</th>
                <th>操作</th>
              </tr>
            </thead>
            <tbody id="glossaryBody">
              <tr id="glossaryNewRow" class="inline-new-row">
                <td><input v-model="inlineTerm.Enabled" type="checkbox"></td>
                <td><input id="glossarySourceTerm" v-model="inlineTerm.SourceTerm" autocomplete="off" placeholder="新增原术语"></td>
                <td><input id="glossaryTargetTerm" v-model="inlineTerm.TargetTerm" autocomplete="off" placeholder="指定译名"></td>
                <td><input id="glossaryTargetLanguage" v-model="inlineTerm.TargetLanguage" autocomplete="off" placeholder="zh-Hans"></td>
                <td>手动</td>
                <td>0</td>
                <td>-</td>
                <td>
                  <div class="table-actions">
                    <button id="saveGlossaryInlineRow" type="button" @click="saveGlossaryTerm()">保存</button>
                    <button class="secondary" type="button" @click="resetInlineTerm">清空</button>
                  </div>
                </td>
              </tr>
              <tr v-for="row in rows" :key="`${row.NormalizedSourceTerm}-${row.TargetLanguage}`">
                <td><input type="checkbox" :checked="row.Enabled" @change="toggleGlossaryTerm(row, ($event.target as HTMLInputElement).checked)"></td>
                <td>{{ row.SourceTerm }}</td>
                <td>{{ row.TargetTerm }}</td>
                <td>{{ row.TargetLanguage }}</td>
                <td>{{ row.Source }}</td>
                <td>{{ row.UsageCount }}</td>
                <td>{{ formatDateTime(row.UpdatedUtc) }}</td>
                <td>
                  <div class="table-actions">
                    <button class="secondary" type="button" @click="editGlossaryTerm(row)">编辑</button>
                    <button class="danger" type="button" @click="deleteGlossaryTerm(row)">删除</button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
        <div class="message" id="glossaryMessage">共 {{ totalCount }} 条术语，当前显示 {{ rows.length }} 条。</div>
      </SectionPanel>
    </div>
  </section>
</template>
