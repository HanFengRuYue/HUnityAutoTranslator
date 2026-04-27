<script setup lang="ts">
import { computed, ref } from "vue";
import AppSidebar from "./components/AppSidebar.vue";
import ToastHost from "./components/ToastHost.vue";
import { controlPanelStore } from "./state/controlPanelStore";
import AboutPage from "./pages/AboutPage.vue";
import AiSettingsPage from "./pages/AiSettingsPage.vue";
import GlossaryPage from "./pages/GlossaryPage.vue";
import PluginSettingsPage from "./pages/PluginSettingsPage.vue";
import StatusPage from "./pages/StatusPage.vue";
import TextEditorPage from "./pages/TextEditorPage.vue";

const pages = {
  status: StatusPage,
  plugin: PluginSettingsPage,
  ai: AiSettingsPage,
  glossary: GlossaryPage,
  editor: TextEditorPage,
  about: AboutPage
};

const sidebarCollapsed = ref(false);
const activePageComponent = computed(() => pages[controlPanelStore.activePage]);
</script>

<template>
  <div class="app-shell" :class="{ 'sidebar-collapsed': sidebarCollapsed }">
    <AppSidebar @update:collapsed="sidebarCollapsed = $event" />
    <main class="workspace">
      <div class="workspace-main">
        <component :is="activePageComponent" />
      </div>
    </main>
    <ToastHost />
  </div>
</template>
