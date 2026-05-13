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
import TexturePage from "./pages/TexturePage.vue";

const pages = {
  status: StatusPage,
  plugin: PluginSettingsPage,
  ai: AiSettingsPage,
  glossary: GlossaryPage,
  textures: TexturePage,
  editor: TextEditorPage,
  about: AboutPage
};

const sidebarCollapsed = ref(false);
const activePageComponent = computed(() => pages[controlPanelStore.activePage]);
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
  <div class="app-shell" :class="{ 'sidebar-collapsed': sidebarCollapsed }">
    <AppSidebar @update:collapsed="sidebarCollapsed = $event" />
    <main class="workspace">
      <div class="workspace-main">
        <Transition name="page-fade" mode="out-in">
          <component :is="activePageComponent" :key="controlPanelStore.activePage" />
        </Transition>
      </div>
    </main>
    <ToastHost />
  </div>
</template>
