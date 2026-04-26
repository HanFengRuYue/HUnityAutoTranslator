# Runtime Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add configurable in-game runtime hotkeys for opening the control panel, source/translation display toggling, full scan/update, and font replacement toggling.

**Architecture:** Persist hotkey binding strings through the existing core control-panel configuration path. Keep action handling in the plugin runtime, with temporary toggle state held only in memory by runtime services.

**Tech Stack:** C# `netstandard2.1`, BepInEx Unity Mono plugin, Unity `Input`, xUnit, FluentAssertions, embedded HTML/JavaScript control panel.

---

### Task 1: Persist Hotkey Bindings

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/Configuration/RuntimeConfig.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/UpdateConfigRequest.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelState.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelService.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs`

- [ ] Write failing tests for default bindings, persistence, and invalid/empty fallback.
- [ ] Run `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests`.
- [ ] Add four binding fields with defaults `Alt+H`, `Alt+F`, `Alt+G`, `Alt+D`.
- [ ] Save and load those fields through `UpdateConfigRequest`.
- [ ] Re-run the focused tests.

### Task 2: Runtime Hotkey Actions

**Files:**
- Create: `src/HUnityAutoTranslator.Plugin/Hotkeys/RuntimeHotkey.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Hotkeys/RuntimeHotkeyController.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Plugin.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Capture/ITextCaptureModule.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Capture/TextCaptureCoordinator.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Capture/UguiTextScanner.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Capture/TmpTextScanner.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Capture/ImguiHookInstaller.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Unity/UnityMainThreadResultApplier.cs`
- Modify: `src/HUnityAutoTranslator.Core/Dispatching/TranslationWritebackTracker.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Unity/UnityTextFontReplacementService.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Runtime/BoundedRuntimeLoopSourceTests.cs`

- [ ] Write failing source tests for hotkey controller wiring, full scan, display toggle, and font toggle.
- [ ] Run the focused runtime tests and confirm they fail.
- [ ] Implement hotkey parsing/matching and call it from `Plugin.Update`.
- [ ] Add full-scan support to capture modules.
- [ ] Add source/translation display toggle to the writeback tracker/applier.
- [ ] Add original/replacement font toggle to the font replacement service.
- [ ] Re-run the focused runtime tests.

### Task 3: Control Panel Settings

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelHtmlSourceTests.cs`

- [ ] Write failing source tests for four hotkey input fields and config payload keys.
- [ ] Run the focused control-panel source tests and confirm they fail.
- [ ] Add the hotkey section to the plugin settings page.
- [ ] Add fields to `textFields`, `readConfig`, and state refresh.
- [ ] Run `node --check` against the extracted embedded script.
- [ ] Re-run the focused source tests.

### Task 4: Final Verification

**Files:**
- No source changes expected.

- [ ] Run `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj`.
- [ ] Run `dotnet build src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj`.
