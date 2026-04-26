# Auto Open Control Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a default-on setting that opens the real loopback control panel URL in the system default browser after the plugin starts successfully.

**Architecture:** Keep configuration and persistence in `HUnityAutoTranslator.Core`, where existing tests already cover control-panel settings. Keep browser launching in `HUnityAutoTranslator.Plugin`, after `LocalHttpServer.Start` selects the actual reachable URL.

**Tech Stack:** C# `netstandard2.1`, BepInEx Unity Mono plugin, `HttpListener`, Newtonsoft JSON, xUnit/FluentAssertions.

---

### Task 1: Persist The Auto-Open Setting

**Files:**
- Modify: `src/HUnityAutoTranslator.Core/Configuration/RuntimeConfig.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/UpdateConfigRequest.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelState.cs`
- Modify: `src/HUnityAutoTranslator.Core/Control/ControlPanelService.cs`
- Test: `tests/HUnityAutoTranslator.Core.Tests/Control/ControlPanelServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Add tests proving the default is on and a saved `false` value reloads.

- [ ] **Step 2: Run the focused tests**

Run: `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests`

Expected: FAIL because `AutoOpenControlPanel` does not exist yet.

- [ ] **Step 3: Add the setting through core state and persistence**

Add `bool AutoOpenControlPanel` to the runtime config, update request, state snapshot, service apply/save paths, and default it to `true`.

- [ ] **Step 4: Run the focused tests again**

Run: `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj --filter ControlPanelServiceTests`

Expected: PASS.

### Task 2: Launch The Default Browser After HTTP Startup

**Files:**
- Modify: `src/HUnityAutoTranslator.Plugin/Plugin.cs`
- Create: `src/HUnityAutoTranslator.Plugin/Web/SystemBrowserLauncher.cs`
- Modify: `src/HUnityAutoTranslator.Plugin/Web/ControlPanelHtml.cs`

- [ ] **Step 1: Implement a small browser launcher**

Create a plugin-local helper that uses `ProcessStartInfo { FileName = url, UseShellExecute = true }` and catches/logs failures without throwing.

- [ ] **Step 2: Call it after the server starts**

In `Plugin.Awake`, after `_httpServer.Start(config.HttpHost, config.HttpPort)` and after the selected URL is available, call the launcher only when `_controlPanel.GetConfig().AutoOpenControlPanel` is true.

- [ ] **Step 3: Add the control-panel switch**

Expose `AutoOpenControlPanel` in the settings payload and add a Chinese checkbox in the plugin settings section.

- [ ] **Step 4: Build the plugin**

Run: `dotnet build src/HUnityAutoTranslator.Plugin/HUnityAutoTranslator.Plugin.csproj`

Expected: exit code 0.

### Task 3: Package Verification

**Files:**
- No source changes expected.

- [ ] **Step 1: Run the full core test suite**

Run: `dotnet test tests/HUnityAutoTranslator.Core.Tests/HUnityAutoTranslator.Core.Tests.csproj`

Expected: exit code 0.

- [ ] **Step 2: Run packaging**

Run: `powershell -ExecutionPolicy Bypass -File build/package.ps1 -Configuration Release`

Expected: exit code 0 and package output under `artifacts`.
