import { createApp } from "vue";
import App from "./App.vue";
import { applyTheme, refreshState } from "./state/controlPanelStore";
import "./styles/tokens.css";
import "./styles/app.css";
import "./styles/polyglot.css";

applyTheme();
window.matchMedia("(prefers-color-scheme: dark)").addEventListener("change", applyTheme);
void refreshState({ quiet: true });
window.setInterval(() => {
  void refreshState({ quiet: true });
}, 5000);

createApp(App).mount("#app");
