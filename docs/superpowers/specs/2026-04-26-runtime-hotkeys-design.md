# Runtime Hotkeys Design

## Goal

Add configurable in-game hotkeys for quick runtime actions: open the control panel, temporarily switch text display between source and translated text, force a full text scan/update, and temporarily switch text component fonts between their original fonts and replacement fonts.

## Design

- Persist only the hotkey bindings in the existing control-panel settings flow. Defaults are `Alt+H`, `Alt+F`, `Alt+G`, and `Alt+D`.
- Keep runtime toggle state in memory only. `Alt+F` and `Alt+D` must not write their current mode to `settings.json`.
- Add a small hotkey parser/matcher that understands modifier chords such as `Alt+H`, `Ctrl+Shift+F`, and `None`. Invalid or empty values fall back to the existing binding.
- Add a plugin-side hotkey controller called from `Plugin.Update`. It reads the current config, ignores repeated key-down frames, and invokes the matching action.
- Open-panel hotkey reuses the existing `SystemBrowserLauncher` and the real `LocalHttpServer.Url`.
- Force scan hotkey calls a full scan path on capture modules and then reapplies remembered writebacks across all known targets.
- Text display toggle asks the Unity writeback tracker to apply either remembered translations or remembered original text across registered targets.
- Font toggle is implemented in the Unity font replacement service by remembering original UGUI/TMP font values when replacement is first applied, then restoring or reapplying them on demand.

## Control Panel

The plugin settings page gets one compact "快捷键" section with four editable fields. The fields save through `/api/config` with the rest of plugin settings. Visible copy stays Chinese-first and short.

## Error Handling

Invalid hotkey text should not break the plugin. Runtime parsing ignores the invalid binding and falls back to the prior/default value. Hotkey actions log concise messages; browser launch failures remain warnings and do not stop translation.

## Tests

- Core tests cover default bindings, persistence, and invalid/empty fallback.
- Source tests cover control-panel fields and plugin hotkey wiring.
- Runtime source tests cover full-scan paths and non-persistent runtime toggles.
