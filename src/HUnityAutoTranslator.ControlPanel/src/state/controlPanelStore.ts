import { reactive } from "vue";
import { api, getJson, postJson } from "../api/client";
import type {
  ConnectionState,
  ControlPanelState,
  FontPickOptions,
  FontPickResult,
  PageKey,
  SelfCheckReport,
  ThemeMode,
  ToastKind,
  UpdateConfigRequest
} from "../types/api";

interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

interface ControlPanelStore {
  activePage: PageKey;
  textureViewMode: "list" | "gallery";
  connection: ConnectionState;
  state: ControlPanelState | null;
  isRefreshing: boolean;
  lastRefreshUtc: string | null;
  lastError: string | null;
  theme: ThemeMode;
  dirtyForms: Set<string>;
  toasts: Toast[];
}

interface SaveOptions {
  quiet?: boolean;
}

const themeStorageKey = "hunity.controlPanel.theme";
let nextToastId = 1;

function loadTheme(): ThemeMode {
  const saved = localStorage.getItem(themeStorageKey);
  return saved === "light" || saved === "dark" || saved === "system" ? saved : "light";
}

export const controlPanelStore = reactive<ControlPanelStore>({
  activePage: "status",
  textureViewMode: "list",
  connection: "connecting",
  state: null,
  isRefreshing: false,
  lastRefreshUtc: null,
  lastError: null,
  theme: loadTheme(),
  dirtyForms: new Set<string>(),
  toasts: []
});

export function setActivePage(page: PageKey): void {
  controlPanelStore.activePage = page;
}

export function setDirtyForm(key: string, dirty: boolean): void {
  if (dirty) {
    controlPanelStore.dirtyForms.add(key);
  } else {
    controlPanelStore.dirtyForms.delete(key);
  }
}

export function showToast(message: string, kind: ToastKind = "info", duration = 3600): number {
  const id = nextToastId++;
  controlPanelStore.toasts.push({ id, kind, message });
  window.setTimeout(() => dismissToast(id), duration);
  return id;
}

export function dismissToast(id: number): void {
  const index = controlPanelStore.toasts.findIndex((toast) => toast.id === id);
  if (index >= 0) {
    controlPanelStore.toasts.splice(index, 1);
  }
}

export function effectiveTheme(theme: ThemeMode = controlPanelStore.theme): "light" | "dark" {
  if (theme === "system") {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  return theme;
}

export function applyTheme(): void {
  document.documentElement.dataset.theme = effectiveTheme();
}

export function cycleTheme(): void {
  const order: ThemeMode[] = ["system", "light", "dark"];
  const next = order[(order.indexOf(controlPanelStore.theme) + 1) % order.length];
  controlPanelStore.theme = next;
  localStorage.setItem(themeStorageKey, next);
  applyTheme();
}

export function markPanelDisconnected(error: unknown): void {
  const message = error instanceof Error ? error.message : "控制面板连接已中断。";
  controlPanelStore.connection = "offline";
  controlPanelStore.lastError = message;
  controlPanelStore.isRefreshing = false;
}

export async function refreshState(options: { quiet?: boolean } = {}): Promise<ControlPanelState | null> {
  if (controlPanelStore.isRefreshing) {
    return controlPanelStore.state;
  }

  controlPanelStore.isRefreshing = true;
  if (!controlPanelStore.state) {
    controlPanelStore.connection = "connecting";
  }

  try {
    const state = await getJson<ControlPanelState>("/api/state");
    controlPanelStore.state = state;
    controlPanelStore.connection = "online";
    controlPanelStore.lastRefreshUtc = new Date().toISOString();
    controlPanelStore.lastError = state.LastError;
    if (!options.quiet) {
      showToast("状态已刷新", "ok", 1800);
    }
    return state;
  } catch (error) {
    markPanelDisconnected(error);
    if (!options.quiet) {
      showToast(controlPanelStore.lastError ?? "刷新失败", "error");
    }
    return null;
  } finally {
    controlPanelStore.isRefreshing = false;
  }
}

export async function getSelfCheck(): Promise<SelfCheckReport | null> {
  try {
    const report = await getJson<SelfCheckReport>("/api/self-check");
    if (controlPanelStore.state) {
      controlPanelStore.state.SelfCheck = report;
    }

    controlPanelStore.connection = "online";
    return report;
  } catch (error) {
    markPanelDisconnected(error);
    showToast(controlPanelStore.lastError ?? "读取本地自检结果失败", "error");
    return null;
  }
}

export async function runSelfCheck(): Promise<SelfCheckReport | null> {
  try {
    const report = await postJson<SelfCheckReport>("/api/self-check/run", {});
    if (controlPanelStore.state) {
      controlPanelStore.state.SelfCheck = report;
    }

    controlPanelStore.connection = "online";
    showToast("本地自检已开始", "info", 2200);
    window.setTimeout(() => {
      void getSelfCheck();
    }, 1800);
    return report;
  } catch (error) {
    markPanelDisconnected(error);
    showToast(controlPanelStore.lastError ?? "启动本地自检失败", "error");
    return null;
  }
}

export async function saveConfig(request: UpdateConfigRequest, formKey?: string, options: SaveOptions = {}): Promise<ControlPanelState | null> {
  try {
    const state = await postJson<ControlPanelState>("/api/config", request);
    controlPanelStore.state = state;
    controlPanelStore.connection = "online";
    controlPanelStore.lastRefreshUtc = new Date().toISOString();
    controlPanelStore.lastError = state.LastError;
    if (formKey) {
      setDirtyForm(formKey, false);
    }
    if (!options.quiet) {
      showToast("设置已保存", "ok");
    }
    return state;
  } catch (error) {
    markPanelDisconnected(error);
    showToast(controlPanelStore.lastError ?? "保存失败", "error");
    return null;
  }
}

export async function saveApiKey(apiKey: string, options: SaveOptions = {}): Promise<ControlPanelState | null> {
  try {
    const state = await postJson<ControlPanelState>("/api/key", { ApiKey: apiKey });
    controlPanelStore.state = state;
    controlPanelStore.connection = "online";
    controlPanelStore.lastRefreshUtc = new Date().toISOString();
    controlPanelStore.lastError = state.LastError;
    if (!options.quiet) {
      showToast(apiKey.trim() ? "API Key 已加密保存" : "API Key 已清除", "ok");
    }
    return state;
  } catch (error) {
    markPanelDisconnected(error);
    showToast(controlPanelStore.lastError ?? "API Key 保存失败", "error");
    return null;
  }
}

export async function saveTextureImageApiKey(apiKey: string, options: SaveOptions = {}): Promise<ControlPanelState | null> {
  try {
    const state = await postJson<ControlPanelState>("/api/texture-image-profiles", { ApiKey: apiKey });
    controlPanelStore.state = state;
    controlPanelStore.connection = "online";
    controlPanelStore.lastRefreshUtc = new Date().toISOString();
    controlPanelStore.lastError = state.LastError;
    if (!options.quiet) {
      showToast(apiKey.trim() ? "贴图图片 API Key 已加密保存" : "贴图图片 API Key 已清除", "ok");
    }
    return state;
  } catch (error) {
    markPanelDisconnected(error);
    showToast(controlPanelStore.lastError ?? "贴图图片 API Key 保存失败", "error");
    return null;
  }
}

export async function pickFontFile(options: FontPickOptions = {}): Promise<FontPickResult> {
  return api<FontPickResult>("/api/fonts/pick", { method: "POST", body: options });
}
