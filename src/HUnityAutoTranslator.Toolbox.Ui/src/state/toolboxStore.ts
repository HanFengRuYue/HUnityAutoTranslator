import { reactive } from "vue";
import type {
  GameInspection,
  GameLibraryEntry,
  InstallPlan,
  LibraryAccent,
  LibraryLayout,
  LibraryPosterSize,
  PageKey,
  PluginConfigPayload,
  ThemeMode,
  Toast,
  ToastKind
} from "../types/api";
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
  pluginSettingsPath: string;
  pluginConfig: PluginConfigPayload["Config"] | null;
  isInspecting: boolean;
  isPlanningInstall: boolean;
  isLoadingPluginConfig: boolean;
  isSavingPluginConfig: boolean;
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
  pluginSettingsPath: "",
  pluginConfig: null,
  isInspecting: false,
  isPlanningInstall: false,
  isLoadingPluginConfig: false,
  isSavingPluginConfig: false,
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
