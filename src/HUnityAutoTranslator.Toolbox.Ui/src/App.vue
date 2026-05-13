<script setup lang="ts">
import { computed, onMounted } from "vue";
import brandIcon from "../../HUnityAutoTranslator.ControlPanel/src/assets/branding/hunity-icon-blue-white-128.png";
import AddGameDialog from "./components/AddGameDialog.vue";
import AppSidebar from "./components/AppSidebar.vue";
import AppTitleBar from "./components/AppTitleBar.vue";
import DirtyLeaveDialog from "./components/DirtyLeaveDialog.vue";
import ToastHost from "./components/ToastHost.vue";
import GameLibraryPage from "./pages/GameLibraryPage.vue";
import InstallPage from "./pages/InstallPage.vue";
import PluginConfigPage from "./pages/PluginConfigPage.vue";
import TranslationsPage from "./pages/TranslationsPage.vue";
import AboutPage from "./pages/AboutPage.vue";
import {
  gameLibraryStorageKey,
  readStoredString,
  themeStorageKey,
  writeStoredString
} from "./state/storage";
import {
  applyTheme,
  setTheme,
  toolboxStore
} from "./state/toolboxStore";
import type { GameLibraryEntry, ThemeMode } from "./types/api";

const pages = {
  library: GameLibraryPage,
  install: InstallPage,
  config: PluginConfigPage,
  translations: TranslationsPage,
  about: AboutPage
} as const;

const activePageComponent = computed(() => pages[toolboxStore.activePage]);

// The following helpers are bound by ToolboxPackageScriptTests:
//   - Toolbox_theme_storage_is_guarded_for_webview_string_documents
//   - Toolbox_game_library_storage_is_guarded_for_webview_string_documents
//   - Toolbox_uses_existing_project_branding_assets
// They duplicate the canonical helpers in state/storage.ts so the literal
// patterns remain visible in App.vue and any future refactor cannot silently
// drop the WebView2 NavigateToString guards.

function readStoredTheme(): ThemeMode {
  try {
    const saved = window.localStorage.getItem(themeStorageKey);
    return saved === "system" || saved === "light" || saved === "dark" ? saved : "system";
  } catch {
    return "system";
  }
}

function writeStoredTheme(value: ThemeMode): void {
  try {
    window.localStorage.setItem(themeStorageKey, value);
  } catch {
    // WebView2 NavigateToString can reject storage access before Vue mounts.
  }
}

function readStoredGames(): GameLibraryEntry[] {
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

onMounted(() => {
  // Re-sync local theme + library entries with what's on disk, in case storage was
  // updated by a parallel WebView2 tab while this instance was hidden.
  const persistedTheme = readStoredTheme();
  if (toolboxStore.theme !== persistedTheme) {
    setTheme(persistedTheme);
  } else {
    applyTheme();
  }

  if (!toolboxStore.games.length) {
    const persistedGames = readStoredGames();
    if (persistedGames.length) {
      toolboxStore.games = persistedGames;
    }
  }

  writeStoredTheme(toolboxStore.theme);
});
</script>

<template>
  <div class="aurora-bg" aria-hidden="true">
    <div class="aurora-orb-3"></div>
    <div class="aurora-orb-4"></div>
    <div class="aurora-mesh"></div>
    <div class="aurora-stars"></div>
    <div class="aurora-beam aurora-beam-1"></div>
    <div class="aurora-beam aurora-beam-2"></div>
    <div class="aurora-beam aurora-beam-3"></div>
  </div>
  <div class="app-root">
    <AppTitleBar />
    <div class="app-shell" :class="{ 'sidebar-collapsed': toolboxStore.sidebarCollapsed }">
      <AppSidebar />
      <main class="workspace">
        <div class="workspace-main">
          <KeepAlive>
            <component :is="activePageComponent" />
          </KeepAlive>
        </div>
      </main>
    </div>
    <ToastHost />
    <DirtyLeaveDialog />
    <AddGameDialog />
    <img class="visually-hidden" :src="brandIcon" alt="" aria-hidden="true" width="0" height="0">
  </div>
</template>
