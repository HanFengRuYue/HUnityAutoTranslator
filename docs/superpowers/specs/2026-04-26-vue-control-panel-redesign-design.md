# Vue Control Panel Redesign Design

Date: 2026-04-26
Status: Design approved for Vue/Vite option 2 and visual direction A, awaiting written spec review

## Goal

Move the localhost control panel UI from a large hand-authored embedded HTML/JavaScript string to a Vue-based front-end source tree, while preserving the current plugin delivery model: the released plugin still serves a static, localhost-only control panel through `LocalHttpServer` and does not depend on external CDNs or runtime package downloads.

The visual redesign uses direction A from the visual companion: a light-first, professional operations console with compact spacing, strong table readability, low visual noise, and a dark theme available through the existing theme cycle.

## Visual Thesis

Build a calm, light-first desktop operations console: crisp surfaces, restrained borders, one blue action accent, dense but readable metric and table layouts, and Chinese-first utility copy for people actively running and editing translations.

## Content Plan

- Runtime status is the default page and shows pipeline health, AI service state, writeback status, and recent completed translations.
- Plugin settings keeps runtime behavior, capture modules, hotkeys, font replacement, cache lookup, and context behavior.
- AI settings keeps provider connection, model presets, concurrency limits, reasoning controls, API key updates, provider utilities, and prompt editing.
- Glossary keeps term settings, manual term entry, search, refresh, and the term table.
- Text editor keeps the SQLite-backed translation table, column visibility, manual column ordering, import/export, search, filters, editing, and save actions.
- Version information keeps plugin, control-panel URL, local-only safety note, and useful runtime metadata.

## Interaction Thesis

- Page navigation should feel immediate and should preserve unsaved form edits until the user saves, refreshes intentionally, or leaves the browser session.
- Live `/api/state` refreshes should update read-only metrics without stealing focus from inputs or overwriting dirty forms.
- Form save actions should produce top toasts, not duplicate inline feedback under cards.
- The editor should continue to behave like a compact spreadsheet: stable column widths, sticky headers, filters, multi-cell selection, copy/paste TSV, row edits, import, and export.
- Theme switching should remain one compact cycle button and should not require backend persistence.

## Architecture

Use a small Vue/Vite front-end source tree and generate the embedded panel used by the plugin.

- Add `src/HUnityAutoTranslator.ControlPanel/` as the front-end source root.
- Use Vite in library-like single-page mode with Vue single-file components or colocated `.vue` components.
- Build to a temporary static output containing one `index.html` with inlined CSS and JavaScript.
- Add a generation step that converts the built HTML into the existing `ControlPanelHtml.cs` constant.
- Keep `LocalHttpServer` serving `ControlPanelHtml.Html` so deployed plugin behavior and URL routing stay stable.
- Do not use CDN-hosted Vue, external fonts, or remote assets.
- Do not echo saved API keys back to browser state; the API key field remains write-only and empty unless the user enters a new key.

This keeps the release artifact simple while making day-to-day UI work component-based.

## Front-End Structure

Initial source layout:

- `src/HUnityAutoTranslator.ControlPanel/package.json`
- `src/HUnityAutoTranslator.ControlPanel/vite.config.ts`
- `src/HUnityAutoTranslator.ControlPanel/index.html`
- `src/HUnityAutoTranslator.ControlPanel/src/main.ts`
- `src/HUnityAutoTranslator.ControlPanel/src/App.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/api/client.ts`
- `src/HUnityAutoTranslator.ControlPanel/src/state/controlPanelStore.ts`
- `src/HUnityAutoTranslator.ControlPanel/src/styles/tokens.css`
- `src/HUnityAutoTranslator.ControlPanel/src/styles/app.css`
- `src/HUnityAutoTranslator.ControlPanel/src/components/AppSidebar.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/components/ToastHost.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/components/MetricCard.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/components/SectionPanel.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/pages/StatusPage.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/pages/PluginSettingsPage.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/pages/AiSettingsPage.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/pages/GlossaryPage.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/pages/TextEditorPage.vue`
- `src/HUnityAutoTranslator.ControlPanel/src/pages/AboutPage.vue`

The Vue store should stay small and explicit. It can use Vue reactivity directly instead of adding Pinia unless the implementation shows real state complexity that Vue primitives cannot handle cleanly.

## Build and Generation

Add a repeatable front-end build path:

- `npm install` creates the initial lockfile in `src/HUnityAutoTranslator.ControlPanel`; after that, `npm ci` is the repeatable install command.
- `npm run build` produces a static single-page control panel with inlined assets.
- A repository script such as `build/generate-control-panel.ps1` reads the built `index.html`, escapes it, and writes `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`.
- The .NET solution build should not require Node unless the generated `ControlPanelHtml.cs` is missing or explicitly regenerated.
- `build/package.ps1` should run the front-end build and generator before `dotnet publish`, so packaged releases always contain the current panel.
- Source tests continue to assert important generated hooks and Chinese copy in `ControlPanelHtml.cs`.

The generated C# file remains reviewable but is treated as generated output. Human-authored UI changes should happen in the Vue source tree.

## Page Design

### Status Page

The status page uses the selected visual direction A:

- Left sidebar shows `HUnityAutoTranslator`, "本机控制面板", page navigation, connection state, and the theme cycle.
- Header shows "运行状态" with a compact refresh button.
- A top metric section uses seven compact cards for plugin status, captured text, waiting translations, in-flight translations, completed translations, writeback queue, and saved translations.
- AI service summary appears as a right-side or secondary section with provider, model, key state, total tokens, average latency, and average speed.
- Recent completed translations remain minimal: source text, translated text, and completion time.
- Hover/focus help text stays available for status metrics and uses content-width tooltips.

### Plugin Settings Page

The settings page groups controls by task:

- Translation behavior: enable translation, target language, source text length, context limits, cache lookup, and context toggle.
- Capture and performance: scan interval, scan target count, writebacks per frame, UGUI, TextMeshPro, IMGUI, manual edits override AI, and remembered translation reapply.
- Hotkeys: open panel, source/translation toggle, force scan, and font toggle.
- Font replacement: font name, font file, sampling size, size adjustment mode/value, font replacement toggles, and automatic CJK fallback.

The visual treatment uses sections, form grids, compact check rows, and no explanatory prose unless it clarifies runtime behavior.

### AI Settings Page

The AI page keeps provider configuration in one coherent workflow:

- Provider identity: provider kind, base URL, endpoint, model preset, model, and API key update field.
- Limits: max concurrent requests, requests per minute, batch character limit, request timeout, and temperature where applicable.
- Reasoning: OpenAI reasoning effort, output verbosity, DeepSeek thinking mode, and DeepSeek reasoning effort.
- Provider utilities: test connection, fetch model list, and query balance/costs, with all feedback routed to top toasts.
- Prompt: prompt style and editable full prompt template.

Provider-specific controls remain conditionally visible and must not reset values simply because a refresh payload omits optional fields.

### Glossary Page

The glossary page keeps the current behavior but improves scanability:

- Settings panel for glossary enablement, automatic extraction, max terms, and max characters.
- Manual term entry panel with source term, target term, target language, note, add/update, and clear.
- Search and refresh toolbar.
- Term table with compact row density and clear delete/edit affordances.

### Text Editor Page

The text editor is the densest page and should receive the most table polish:

- Search, column display, clear filters, import, and export stay in a single toolbar.
- Column chooser persists visibility separately from manual order.
- Header filter buttons stay small and icon-like; selected filters get a subtle active state.
- Table cells use stable dimensions so selection, dirty markers, and hover states do not resize rows.
- Editable translated text and context cells keep spreadsheet-style selection and TSV copy/paste.
- Import/export remains a two-button surface: import and export, with export format in a small menu.

### About Page

The about page stays compact:

- Plugin name and version.
- Control panel URL.
- Settings/cache location if available in state.
- Runtime target and build channel if available.
- Local-only safety note.
- Provider documentation references as plain text or non-sensitive links.

## Styling System

Use CSS custom properties as design tokens:

- Light default: `--bg`, `--sidebar`, `--surface`, `--surface-2`, `--field`, `--line`, `--line-strong`, `--ink`, `--muted`, `--accent`, `--accent-hover`, `--accent-soft`, `--warn`, `--ok`, `--danger`, and `--shadow`.
- Dark theme mirrors the same tokens and is activated with `data-theme="dark"`.
- System theme follows `prefers-color-scheme` when no explicit light/dark choice is selected.
- Dominant palette remains neutral with one blue accent; avoid green-black, purple-blue gradient, beige, brown/orange, and decorative orb styling.
- Cards are limited to real repeated items, metric cards, menus, modals, and tool surfaces.
- Page sections use full-width bands within the work surface rather than nested card stacks.

Text sizing stays fixed and responsive through layout, not viewport-scaled fonts.

## API and Data Flow

The Vue app keeps the existing HTTP API contracts:

- `GET /api/state`
- `POST /api/config`
- `POST /api/key`
- `GET /api/translations`
- `PATCH /api/translations`
- `GET /api/translations/export?format=json|csv`
- `POST /api/translations/import`
- `GET /api/provider/models`
- `GET /api/provider/balance`
- `POST /api/provider/test`
- Glossary endpoints already used by the current panel

The store maintains:

- `state`: last successful `/api/state` payload.
- `connection`: connected, reconnecting, or disconnected.
- `activePage`: selected page.
- `configDirty`: whether settings forms should ignore automatic refresh values.
- `toasts`: top feedback messages.
- `translations`: current editor rows, filters, selection, column order, visible columns, and dirty cell state.
- `glossary`: terms, search text, dirty state, and feedback state.

Refresh failure must mark all visible status surfaces as disconnected so stale "运行中" state does not remain visible.

## Error Handling

- API failures show concise Chinese toasts and update the sidebar connection state.
- Refresh failures do not overwrite dirty form fields.
- Save failures keep user-entered values visible for correction.
- Provider utility failures show only sanitized messages and never print API keys.
- Import failures explain the validation issue before any database modification.
- If Vue fails to mount, the generated page should show a short Chinese fallback message that asks the user to reload the local panel.

## Testing and Verification

Implementation verification should include:

- `npm run build` in the Vue control-panel source root.
- A generator check that `ControlPanelHtml.cs` is updated deterministically.
- `node --check` or equivalent syntax validation for generated inline JavaScript if the output remains inspectable.
- Existing source tests in `ControlPanelHtmlSourceTests` updated to assert generated DOM hooks, Chinese labels, toast routing, theme cycle, column layout keys, and no removed inline provider feedback.
- Focused .NET tests for API/state behavior if implementation touches backend contracts.
- Full `dotnet test HUnityAutoTranslator.sln` before packaging.
- `build/package.ps1` verification that the package contains the regenerated control panel and the required plugin/runtime files.
- Browser smoke test against the local panel for desktop and narrow widths, checking status page, AI page, editor page, theme cycle, and toast behavior.

## Migration Strategy

Implement in small steps:

- First add the Vue project, build config, and generator while preserving current generated HTML behavior.
- Then port shared shell pieces: sidebar, theme, toast host, API client, and status page.
- Next port settings pages and provider utilities.
- Then port glossary and editor, preserving table storage keys and existing editor behavior.
- Finally switch packaging to regenerate before publish and update source tests to treat generated output as the deployed contract.

The old hand-authored panel should not be deleted until the generated Vue output reaches parity for all current pages and tests pass.

## Implementation Decisions

- The project should start without Pinia; add it only if Vue primitives create repeated state coordination problems during implementation.
- The generated `ControlPanelHtml.cs` should stay committed so .NET-only builds work after checkout.
- The Vue project should commit lockfile data so builds are repeatable on this Windows machine and CI-like local runs.
