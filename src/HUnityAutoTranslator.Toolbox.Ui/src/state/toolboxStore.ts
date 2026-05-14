import { reactive } from "vue";
import type {
  CustomInstallOptions,
  EmbeddedAssetInfo,
  GameInspection,
  GameLibraryEntry,
  InstallCancelledPayload,
  InstallCompletedPayload,
  InstallFailedPayload,
  InstallPlan,
  InstallProgressPayload,
  InstallRunState,
  LibraryAccent,
  LibraryLayout,
  LibraryPosterSize,
  PageKey,
  PluginConfigPayload,
  ThemeMode,
  Toast,
  ToastKind
} from "../types/api";
import { invokeToolbox, toolboxEvents } from "../bridge";
import {
  gameLibraryStorageKey,
  libraryAccentStorageKey,
  libraryLayoutStorageKey,
  libraryPosterSizeStorageKey,
  readStoredBool,
  readStoredOption,
  readStoredString,
  selectedGameStorageKey,
  sidebarStorageKey,
  themeStorageKey,
  writeStoredString
} from "./storage";

interface DirtyDialogState {
  open: boolean;
  pendingPage: PageKey | null;
}

interface AddGameDialogState {
  open: boolean;
  initialPath: string;
}

interface ToolboxStore {
  activePage: PageKey;
  theme: ThemeMode;
  sidebarCollapsed: boolean;
  games: GameLibraryEntry[];
  selectedGameId: string;
  libraryLayout: LibraryLayout;
  libraryPosterSize: LibraryPosterSize;
  libraryAccent: LibraryAccent;
  installPlan: InstallPlan | null;
  installRun: InstallRunState | null;
  embeddedBundle: EmbeddedAssetInfo[] | null;
  pluginSettingsPath: string;
  pluginConfig: PluginConfigPayload["Config"] | null;
  isInspecting: boolean;
  isPlanningInstall: boolean;
  isLoadingPluginConfig: boolean;
  isSavingPluginConfig: boolean;
  isRollingBack: boolean;
  dirtyForms: Set<string>;
  toasts: Toast[];
  lastError: string | null;
  dirtyDialog: DirtyDialogState;
  addGameDialog: AddGameDialogState;
}

let nextToastId = 1;
const dirtySaveHandlers = new Map<string, () => Promise<void> | void>();

function loadTheme(): ThemeMode {
  const saved = readStoredString(themeStorageKey);
  return saved === "light" || saved === "dark" || saved === "system" ? saved : "system";
}

function loadGames(): GameLibraryEntry[] {
  try {
    const parsed = JSON.parse(readStoredString(gameLibraryStorageKey) || "[]");
    if (!Array.isArray(parsed)) {
      return [];
    }

    return parsed
      .filter((item): item is GameLibraryEntry =>
        Boolean(item) &&
        typeof item.Id === "string" &&
        typeof item.Root === "string" &&
        typeof item.Name === "string")
      .map((item) => ({
        ...item,
        Inspection: item.Inspection ?? null
      }));
  } catch {
    writeStoredString(gameLibraryStorageKey, "");
    return [];
  }
}

export const toolboxStore = reactive<ToolboxStore>({
  activePage: "library",
  theme: loadTheme(),
  sidebarCollapsed: readStoredBool(sidebarStorageKey, false),
  games: loadGames(),
  selectedGameId: readStoredString(selectedGameStorageKey),
  libraryLayout: readStoredOption(libraryLayoutStorageKey, ["grid", "list"] as const, "grid"),
  libraryPosterSize: readStoredOption(libraryPosterSizeStorageKey, ["compact", "normal", "large"] as const, "normal"),
  libraryAccent: readStoredOption(libraryAccentStorageKey, ["blue", "green", "amber", "rose"] as const, "blue"),
  installPlan: null,
  installRun: null,
  embeddedBundle: null,
  pluginSettingsPath: "",
  pluginConfig: null,
  isInspecting: false,
  isPlanningInstall: false,
  isLoadingPluginConfig: false,
  isSavingPluginConfig: false,
  isRollingBack: false,
  dirtyForms: new Set<string>(),
  toasts: [],
  lastError: null,
  dirtyDialog: { open: false, pendingPage: null },
  addGameDialog: { open: false, initialPath: "" }
});

export function selectedGame(): GameLibraryEntry | null {
  return toolboxStore.games.find((game) => game.Id === toolboxStore.selectedGameId) ?? null;
}

export function selectedInspection(): GameInspection | null {
  return selectedGame()?.Inspection ?? null;
}

export function setActivePage(page: PageKey): void {
  toolboxStore.activePage = page;
}

export function requestNavigation(page: PageKey): void {
  if (toolboxStore.activePage === page) {
    return;
  }

  if (toolboxStore.dirtyForms.size === 0) {
    setActivePage(page);
    return;
  }

  toolboxStore.dirtyDialog.open = true;
  toolboxStore.dirtyDialog.pendingPage = page;
}

export function discardDirtyLeave(): void {
  const pending = toolboxStore.dirtyDialog.pendingPage;
  toolboxStore.dirtyForms.clear();
  toolboxStore.dirtyDialog.open = false;
  toolboxStore.dirtyDialog.pendingPage = null;
  if (pending) {
    setActivePage(pending);
  }
}

export async function saveThenLeaveDirty(): Promise<void> {
  const pending = toolboxStore.dirtyDialog.pendingPage;
  const formKeys = Array.from(toolboxStore.dirtyForms);
  for (const key of formKeys) {
    const handler = dirtySaveHandlers.get(key);
    if (handler) {
      try {
        await handler();
      } catch (error) {
        const message = error instanceof Error ? error.message : "保存失败。";
        showToast(`保存 ${key} 失败：${message}`, "error");
        return;
      }
    }
  }
  toolboxStore.dirtyDialog.open = false;
  toolboxStore.dirtyDialog.pendingPage = null;
  if (pending) {
    setActivePage(pending);
  }
}

export function cancelDirtyLeave(): void {
  toolboxStore.dirtyDialog.open = false;
  toolboxStore.dirtyDialog.pendingPage = null;
}

export function openAddGameDialog(initialPath = ""): void {
  toolboxStore.addGameDialog.initialPath = initialPath;
  toolboxStore.addGameDialog.open = true;
}

export function closeAddGameDialog(): void {
  toolboxStore.addGameDialog.open = false;
  toolboxStore.addGameDialog.initialPath = "";
}

export function setDirtyForm(key: string, dirty: boolean, saveHandler?: () => Promise<void> | void): void {
  if (dirty) {
    toolboxStore.dirtyForms.add(key);
    if (saveHandler) {
      dirtySaveHandlers.set(key, saveHandler);
    }
  } else {
    toolboxStore.dirtyForms.delete(key);
    dirtySaveHandlers.delete(key);
  }
}

export function setSidebarCollapsed(value: boolean): void {
  toolboxStore.sidebarCollapsed = value;
  writeStoredString(sidebarStorageKey, String(value));
}

export function setLibraryLayout(value: LibraryLayout): void {
  toolboxStore.libraryLayout = value;
  writeStoredString(libraryLayoutStorageKey, value);
}

export function setLibraryPosterSize(value: LibraryPosterSize): void {
  toolboxStore.libraryPosterSize = value;
  writeStoredString(libraryPosterSizeStorageKey, value);
}

export function setLibraryAccent(value: LibraryAccent): void {
  toolboxStore.libraryAccent = value;
  writeStoredString(libraryAccentStorageKey, value);
}

export function persistGames(): void {
  writeStoredString(gameLibraryStorageKey, JSON.stringify(toolboxStore.games));
}

export function persistSelectedGame(): void {
  writeStoredString(selectedGameStorageKey, toolboxStore.selectedGameId);
}

export function setSelectedGameId(id: string): void {
  toolboxStore.selectedGameId = id;
  toolboxStore.installPlan = null;
  persistSelectedGame();
}

export function upsertGame(entry: GameLibraryEntry): void {
  const existing = toolboxStore.games.find((game) => game.Id === entry.Id);
  toolboxStore.games = existing
    ? toolboxStore.games.map((game) => game.Id === entry.Id ? entry : game)
    : [entry, ...toolboxStore.games];
  toolboxStore.selectedGameId = entry.Id;
  persistGames();
  persistSelectedGame();
}

export function removeGame(id: string): void {
  toolboxStore.games = toolboxStore.games.filter((game) => game.Id !== id);
  if (toolboxStore.selectedGameId === id) {
    toolboxStore.selectedGameId = "";
    toolboxStore.installPlan = null;
  }
  persistGames();
  persistSelectedGame();
}

export function effectiveTheme(theme: ThemeMode = toolboxStore.theme): "light" | "dark" {
  if (theme === "system") {
    return window.matchMedia?.("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }

  return theme;
}

export function applyTheme(): void {
  document.documentElement.dataset.theme = effectiveTheme();
}

export function setTheme(value: ThemeMode): void {
  toolboxStore.theme = value;
  writeStoredString(themeStorageKey, value);
  applyTheme();
}

export function cycleTheme(): void {
  const order: ThemeMode[] = ["system", "light", "dark"];
  const next = order[(order.indexOf(toolboxStore.theme) + 1) % order.length];
  setTheme(next);
}

export function showToast(message: string, kind: ToastKind = "info", duration = 3600): number {
  const id = nextToastId++;
  toolboxStore.toasts.push({ id, kind, message });
  window.setTimeout(() => dismissToast(id), duration);
  return id;
}

export function dismissToast(id: number): void {
  const index = toolboxStore.toasts.findIndex((toast) => toast.id === id);
  if (index >= 0) {
    toolboxStore.toasts.splice(index, 1);
  }
}

export function setLastError(error: unknown): void {
  const message = error instanceof Error ? error.message : typeof error === "string" ? error : null;
  toolboxStore.lastError = message;
}

export function defaultCustomInstallOptions(): CustomInstallOptions {
  return {
    mode: "Full",
    includeLlamaCppBackend: false,
    llamaCppBackend: "Cuda13",
    runtimeOverride: "",
    bepInExHandling: "Auto",
    backupPolicy: "Auto",
    customPluginDirectory: "",
    customBackupDirectory: "",
    customPluginZipPath: "",
    customBepInExZipPath: "",
    customLlamaCppZipPath: "",
    customUnityLibraryZipPath: "",
    unityVersionOverride: "",
    dryRun: false,
    forceReinstall: false,
    skipPostInstallVerification: false
  };
}

export function countNonDefaultCustomOptions(options: CustomInstallOptions): number {
  const defaults = defaultCustomInstallOptions();
  let changed = 0;
  (Object.keys(defaults) as Array<keyof CustomInstallOptions>).forEach((key) => {
    if (options[key] !== defaults[key]) changed++;
  });
  return changed;
}

export function resetInstallRun(): void {
  toolboxStore.installRun = null;
}

function buildExecutePayload(gameRoot: string, options: CustomInstallOptions, packageVersion: string): Record<string, unknown> {
  return {
    gameRoot,
    packageVersion,
    mode: options.mode,
    includeLlamaCppBackend: options.includeLlamaCppBackend,
    llamaCppBackend: options.llamaCppBackend,
    runtimeOverride: options.runtimeOverride || "",
    bepInExHandling: options.bepInExHandling,
    backupPolicy: options.backupPolicy,
    customPluginDirectory: options.customPluginDirectory,
    customBackupDirectory: options.customBackupDirectory,
    customPluginZipPath: options.customPluginZipPath,
    customBepInExZipPath: options.customBepInExZipPath,
    customLlamaCppZipPath: options.customLlamaCppZipPath,
    customUnityLibraryZipPath: options.customUnityLibraryZipPath,
    unityVersionOverride: options.unityVersionOverride,
    dryRun: options.dryRun,
    forceReinstall: options.forceReinstall,
    skipPostInstallVerification: options.skipPostInstallVerification
  };
}

export async function startInstall(gameRoot: string, options: CustomInstallOptions, packageVersion: string): Promise<boolean> {
  if (toolboxStore.installRun && toolboxStore.installRun.status === "running") {
    showToast("已有安装任务在运行,请先等其完成或取消。", "warn");
    return false;
  }

  try {
    const response = await invokeToolbox<{ runId: string; plan: InstallPlan }>(
      "executeInstallPlan",
      buildExecutePayload(gameRoot, options, packageVersion)
    );
    if (!response || !response.runId || !response.plan) {
      showToast("无法启动安装任务。", "error");
      return false;
    }

    toolboxStore.installRun = {
      id: response.runId,
      plan: response.plan,
      status: "running",
      progress: {
        operationIndex: 0,
        operationCount: response.plan.Operations.length,
        stage: "Preparing",
        message: "准备中",
        percent: 0
      },
      perStepStatus: response.plan.Operations.map(() => "pending"),
      result: null,
      error: null,
      startedAt: new Date().toISOString()
    };
    toolboxStore.installPlan = response.plan;
    setLastError(null);
    return true;
  } catch (error) {
    setLastError(error);
    const message = error instanceof Error ? error.message : "启动安装失败";
    showToast(message, "error");
    return false;
  }
}

export async function cancelCurrentInstall(): Promise<void> {
  const run = toolboxStore.installRun;
  if (!run || run.status !== "running") return;
  try {
    await invokeToolbox<null>("cancelInstall", { runId: run.id });
  } catch (error) {
    setLastError(error);
  }
}

export async function rollbackCurrentInstall(gameRoot: string): Promise<boolean> {
  const run = toolboxStore.installRun;
  const backupDirectory = run?.error?.backupDirectory ?? run?.plan.BackupDirectory ?? null;
  if (!backupDirectory) {
    showToast("没有可用的备份目录可回滚。", "warn");
    return false;
  }

  toolboxStore.isRollingBack = true;
  try {
    const result = await invokeToolbox<{ Succeeded: boolean; RestoredPaths: string[]; Errors: string[] } | null>(
      "rollbackInstall",
      { gameRoot, backupDirectory }
    );
    if (result?.Succeeded) {
      showToast("已回滚到上次备份。", "ok");
      toolboxStore.installRun = null;
      return true;
    }
    const message = result?.Errors?.join("; ") ?? "回滚失败,请查看日志。";
    showToast(message, "error");
    return false;
  } catch (error) {
    setLastError(error);
    showToast(error instanceof Error ? error.message : "回滚失败", "error");
    return false;
  } finally {
    toolboxStore.isRollingBack = false;
  }
}

export async function loadEmbeddedBundleInfo(): Promise<void> {
  try {
    const bundle = await invokeToolbox<EmbeddedAssetInfo[]>("getEmbeddedBundleInfo");
    toolboxStore.embeddedBundle = bundle ?? [];
  } catch (error) {
    setLastError(error);
    toolboxStore.embeddedBundle = [];
  }
}

let installEventsBound = false;
export function bindInstallEvents(): void {
  if (installEventsBound) return;
  installEventsBound = true;
  const events = toolboxEvents();
  events.addEventListener("installProgress", handleInstallProgress as EventListener);
  events.addEventListener("installCompleted", handleInstallCompleted as EventListener);
  events.addEventListener("installFailed", handleInstallFailed as EventListener);
  events.addEventListener("installCancelled", handleInstallCancelled as EventListener);
}

function handleInstallProgress(event: Event): void {
  const detail = (event as CustomEvent).detail as InstallProgressPayload;
  const run = toolboxStore.installRun;
  if (!run || run.id !== detail.runId || run.status !== "running") return;
  run.progress = {
    operationIndex: detail.operationIndex,
    operationCount: detail.operationCount,
    stage: detail.stage,
    message: detail.message,
    percent: detail.percent,
    currentDestination: detail.currentDestination
  };
  if (detail.operationIndex >= 1 && detail.operationIndex <= run.perStepStatus.length) {
    for (let i = 0; i < detail.operationIndex - 1; i++) {
      if (run.perStepStatus[i] === "pending" || run.perStepStatus[i] === "running") {
        run.perStepStatus[i] = "done";
      }
    }
    if (run.perStepStatus[detail.operationIndex - 1] !== "failed") {
      run.perStepStatus[detail.operationIndex - 1] = detail.stage === "Completed" ? "done" : "running";
    }
  }
}

function handleInstallCompleted(event: Event): void {
  const detail = (event as CustomEvent).detail as InstallCompletedPayload;
  const run = toolboxStore.installRun;
  if (!run || run.id !== detail.runId) return;
  run.status = "succeeded";
  run.result = detail.result;
  run.perStepStatus = run.perStepStatus.map((status) => (status === "pending" || status === "running" ? "done" : status));
  run.progress = {
    operationIndex: run.plan.Operations.length,
    operationCount: run.plan.Operations.length,
    stage: "Completed",
    message: detail.result?.Message ?? "安装完成",
    percent: 1
  };
  showToast(detail.result?.Message ?? "安装完成。", "ok");
}

function handleInstallFailed(event: Event): void {
  const detail = (event as CustomEvent).detail as InstallFailedPayload;
  const run = toolboxStore.installRun;
  if (!run || run.id !== detail.runId) return;
  run.status = "failed";
  run.error = {
    message: detail.error,
    stage: detail.stage,
    operationIndex: detail.operationIndex,
    backupDirectory: detail.backupDirectory
  };
  const failedIndex = detail.operationIndex - 1;
  if (failedIndex >= 0 && failedIndex < run.perStepStatus.length) {
    run.perStepStatus[failedIndex] = "failed";
    for (let i = failedIndex + 1; i < run.perStepStatus.length; i++) {
      run.perStepStatus[i] = "skipped";
    }
  }
  showToast(detail.error || "安装失败。", "error");
}

function handleInstallCancelled(event: Event): void {
  const detail = (event as CustomEvent).detail as InstallCancelledPayload;
  const run = toolboxStore.installRun;
  if (!run || run.id !== detail.runId) return;
  run.status = "cancelled";
  run.error = {
    message: "安装已取消",
    stage: "Cancelled",
    operationIndex: detail.operationIndex,
    backupDirectory: detail.backupDirectory
  };
  const lastIndex = detail.operationIndex - 1;
  if (lastIndex >= 0 && lastIndex < run.perStepStatus.length) {
    run.perStepStatus[lastIndex] = "skipped";
    for (let i = lastIndex + 1; i < run.perStepStatus.length; i++) {
      run.perStepStatus[i] = "skipped";
    }
  }
  showToast("安装已取消。", "warn");
}
