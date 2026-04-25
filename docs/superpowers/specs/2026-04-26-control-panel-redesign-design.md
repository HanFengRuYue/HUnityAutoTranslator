# HUnityAutoTranslator Control Panel Redesign

Date: 2026-04-26
Status: Design approved for option A, awaiting written spec review

## Goal

Redesign the local HTTP control panel from a single configuration page into a compact operational console with a fixed left sidebar, five functional pages, and a bottom theme selector. The panel remains a localhost-only static HTML application served by the plugin; it must not depend on external CDNs and must never echo API keys back to the browser.

## Visual Thesis

Build a quiet desktop-style operations console: dense, readable, low-chrome, with a dark/light adaptive surface, teal as the single action accent, and table-first layouts for data-heavy work.

## Content Plan

- Status: live plugin health, translation pipeline counters, AI service state, and recent completed translations.
- Plugin Settings: runtime behavior, capture modules, translation behavior, performance budgets, cache behavior, and safety filters.
- AI Translation Settings: provider connection, model controls, reasoning controls, model list lookup, account/cost lookup, and prompt editing.
- Text Editor: Excel-like database table with filtering, sorting, cell selection, copy/paste, row editing, import, and export.
- About: version, build, environment, links, and placeholder metadata.

## Interaction Thesis

- Sidebar page switching should be instant and preserve unsaved form edits inside the active browser session.
- Live status refresh should update read-only counters without stealing focus from editable fields.
- Table interactions should favor familiar spreadsheet gestures: click cells, shift/ctrl multi-select, copy TSV, paste TSV into editable cells, and use header controls for sort/filter.

## Recommended Architecture

Use option A from the visual companion: a fixed sidebar plus a main work surface.

- Left sidebar contains the product name, five page buttons, connection status, and a bottom theme segmented control.
- Main area renders one page at a time. Pages are regular `<section>` views toggled by JavaScript.
- The current static HTML remains embedded in `ControlPanelHtml.cs`, but its structure is rewritten as a small single-page app.
- Backend JSON APIs stay under `/api/*` on the existing `LocalHttpServer`.
- API keys are accepted by `/api/key` and used server-side only.

## Page Design

### 1. Status Page

The status page is the first page and the default route.

It shows:
- Plugin enabled/paused state.
- Captured text count.
- Waiting queue count.
- In-flight translation count.
- Completed translation count.
- Cache entry count.
- Writeback backlog count.
- AI service state: provider, model, endpoint, API key configured, last check state, and last error.
- Recent completed translations, capped to the latest 8 to 12 rows, with source text, translated text, component/context, provider/model, and completion time.

Backend requirements:
- Add a lightweight metrics object that can be updated by the pipeline, queue, worker pool, and result dispatcher.
- Do not infer completed counts from cache count only; cache contains historical entries and may include imported/manual data.
- Keep recent translation previews in memory and never include API request headers or keys.

### 2. Plugin Settings Page

This page owns non-provider plugin behavior.

Settings:
- Enable translation.
- Target language.
- Translation style.
- Capture modules: UGUI, TextMeshPro, IMGUI.
- Scan interval in milliseconds.
- Max scan targets per tick.
- Max writebacks per frame.
- Max source text length.
- Ignore invisible UI text.
- Skip numeric/symbol-only text.
- Cache lookup enabled.
- Manual edits override AI result.
- Reapply remembered translations.
- Cache retention days as a stored setting for later cleanup support.

Implementation notes:
- Existing runtime settings stay hot-reloadable.
- New settings that affect existing behavior should be wired immediately where low risk.
- Settings that are useful but not yet supported by the runtime can be persisted and shown as "stored for next supported build" only if implementing the runtime behavior would broaden the change too much.

### 3. AI Translation Settings Page

This page owns provider behavior and prompt policy.

Settings:
- Provider kind: OpenAI Responses, DeepSeek, OpenAI-compatible.
- Base URL.
- Endpoint.
- Model.
- API key update field.
- Max concurrent requests.
- Requests per minute.
- Max batch characters.
- Request timeout seconds.
- Reasoning effort.
- Output verbosity.
- DeepSeek thinking mode.
- Temperature for non-thinking compatible providers.
- Prompt style.
- Custom instruction.
- Editable prompt preview.

Provider utilities:
- "Fetch Models" calls the active provider through the local backend.
- "Check Balance / Costs" calls the active provider through the local backend.
- "Test Connection" validates the selected base URL, auth, model endpoint, and API key status without sending game text.

Provider-specific details:
- OpenAI: use the official `/models` endpoint for model list; use organization cost endpoints as best-effort account cost lookup because balance is not a general model endpoint; use `reasoning.effort` values `low`, `medium`, `high`, and `xhigh` for Responses API.
- DeepSeek: use `/models` for model list and `/user/balance` for current balance; expose Thinking Mode using `thinking.type` and `reasoning_effort`, with `high` and `max` as primary choices.
- OpenAI-compatible: use a configurable models path defaulting to `/v1/models`; balance lookup is optional because compatible providers differ.

Sources checked:
- OpenAI official docs: "Using GPT-5.5 / Using reasoning models" says Responses API supports `reasoning.effort` values `low`, `medium`, `high`, and `xhigh`.
- OpenAI official OpenAPI endpoint list includes `/models`, `/organization/costs`, `/organization/usage/completions`, and `/responses`.
- DeepSeek official docs list `GET /models`, `GET /user/balance`, and Thinking Mode controls with `thinking.type` and `reasoning_effort`.

### 4. Text Editor Page

This page presents database content in an Excel-like grid.

Columns:
- Source text.
- Translated text.
- Target language.
- Provider kind.
- Model.
- Scene name.
- Component hierarchy.
- Component type.
- Created UTC.
- Updated UTC.

Table functions:
- Sticky header.
- Per-column filter controls.
- Header click sort.
- Pagination or capped virtual-like rendering for large caches.
- Cell selection.
- Shift range selection.
- Ctrl multi-selection.
- Copy selected cells as TSV.
- Paste TSV into selected editable cells.
- Inline editing for translated text and context columns.
- Save modified rows.
- Refresh from database.
- Export JSON.
- Export CSV.
- Import JSON/CSV with a preview step and explicit confirmation before overwriting or adding rows.

Backend requirements:
- Extend the cache abstraction with query, update, import, and export operations.
- SQLite cache is the primary implementation.
- Memory and disk cache implementations can provide simple in-memory equivalents for tests and compatibility.
- Import must validate columns before modifying the database.
- Export must avoid API keys and provider secrets.

### 5. About Page

This page is mostly placeholder metadata for now.

Fields:
- Plugin name.
- Plugin version placeholder.
- Build channel placeholder.
- Runtime target placeholder.
- Control panel URL.
- Cache location placeholder if available.
- Settings location placeholder if available.
- Provider docs links as plain text.
- Short safety note that the panel is loopback-only.

## Theme Selector

The sidebar bottom contains three options:
- System.
- Dark.
- Light.

Theme behavior:
- Store the selected theme in `localStorage`.
- `system` follows `prefers-color-scheme`.
- `dark` and `light` set `data-theme` on the document root.
- Theme switching is instant and does not require backend persistence.

## Backend API Surface

Keep existing endpoints:
- `GET /api/state`.
- `POST /api/config`.
- `POST /api/key`.

Add endpoints:
- `GET /api/translations`.
- `PATCH /api/translations`.
- `GET /api/translations/export?format=json|csv`.
- `POST /api/translations/import`.
- `GET /api/provider/models`.
- `GET /api/provider/balance`.
- `POST /api/provider/test`.

State payload additions:
- `CapturedTextCount`.
- `QueuedTextCount`.
- `InFlightTranslationCount`.
- `CompletedTranslationCount`.
- `WritebackQueueCount`.
- `RecentTranslations`.
- `ProviderStatus`.
- New plugin and AI settings fields.

## Data Flow

Captured text enters `TextPipeline`, which increments capture metrics after filtering decisions are made. Cache hits increment completed counters and can be reported as completed with source "cache". Queue submissions update queued counts through `TranslationJobQueue`. Worker dequeue/complete operations update in-flight counts. Successful provider results update cache, dispatcher, completed counters, and recent translation previews.

The control panel reads `/api/state` every two seconds. Provider utility actions are manual and never run on each refresh.

## Error Handling

- Provider utility endpoints return structured errors with a user-facing message and HTTP status.
- Failed model/balance lookups do not change saved provider settings.
- Config saves clamp numeric fields on the backend.
- Import failures return validation details and do not partially modify the database.
- Table save failures keep dirty markers visible in the UI.

## Security And Privacy

- Preserve loopback-only access checks.
- Never return API key values or previews.
- Do not log request headers or API keys.
- Provider utility calls are made by the local backend to avoid exposing credentials to browser JavaScript.
- Exported translation data can include game text and context, but not secrets.

## Testing Strategy

Automated tests:
- Control panel service persists and reports new settings.
- Queue metrics report pending and in-flight counts.
- Worker pool records completed translations and recent previews.
- SQLite cache can query, update, export, and import rows.
- Provider utility parsing handles OpenAI/DeepSeek model lists and DeepSeek balance responses.

Manual validation:
- Open the control panel in a browser and switch all five pages.
- Toggle system/dark/light themes.
- Save plugin settings and confirm `/api/state` reflects them.
- Save AI settings without exposing API key values.
- Sort/filter/edit/copy/paste table cells.
- Export and import a small translation set.

## Scope Boundaries

In scope:
- Real page split and sidebar navigation.
- Real local backend APIs for model list and balance/cost lookup where provider support exists.
- Real SQLite-backed text editing and import/export.
- Real theme selector.
- Expanded persisted settings.

Out of scope for this pass:
- Remote access to the control panel.
- Full virtual scrolling for extremely large caches.
- Authentication beyond loopback-only access.
- Guaranteed OpenAI account balance value, because OpenAI exposes cost/usage endpoints rather than a universal balance endpoint.
- Destructive cache clearing unless separately confirmed and implemented with an explicit confirmation UI.
