import { createApp } from "vue";
import App from "./App.vue";
import { applyTheme } from "./state/toolboxStore";
import "./styles/tokens.css";
import "./styles/app.css";
import "./styles/polyglot.css";

applyTheme();
window.matchMedia?.("(prefers-color-scheme: dark)").addEventListener("change", applyTheme);

createApp(App).mount("#app");
