import type {
  DeleteResult,
  EmbeddedAssetInfo,
  GameInspection,
  InstallPlan,
  PluginConfigPayload,
  RollbackResult,
  TranslationCacheFilterOptionPage,
  TranslationCacheImportResult,
  TranslationCachePage
} from "./types/api";

interface NativeBridge {
  invoke<T>(command: string, payload?: unknown): Promise<T>;
  events?: EventTarget;
}

declare global {
  interface Window {
    ToolboxBridge?: NativeBridge;
  }
}

const fallbackDelay = 120;

// Dev 事件总线: 当工具箱在 vite dev 服务器里跑(没有 WebView2 宿主)时,
// 用这个 EventTarget 模拟主机推送的安装进度/完成/取消事件。
export const devEventBus = new EventTarget();

function devDispatch(type: string, detail: Record<string, unknown>): void {
  const event = new CustomEvent(type, { detail: { type, ...detail } });
  devEventBus.dispatchEvent(event);
}

export function toolboxEvents(): EventTarget {
  return window.ToolboxBridge?.events ?? devEventBus;
}

export async function invokeToolbox<T>(command: string, payload: unknown = {}): Promise<T> {
  if (window.ToolboxBridge) {
    return window.ToolboxBridge.invoke<T>(command, payload);
  }

  await new Promise((resolve) => window.setTimeout(resolve, fallbackDelay));
  return fallbackResponse<T>(command, payload);
}

function fallbackResponse<T>(command: string, payload: unknown): T {
  if (command === "getAppInfo") {
    return { Name: "HUnityAutoTranslator 工具箱", Version: "0.1.1" } as T;
  }

  if (command === "inspectGame") {
    const gameRoot = readPayloadString(payload, "gameRoot");
    if (!gameRoot.trim()) {
      return {
        GameRoot: "",
        DirectoryExists: false,
        IsValidUnityGame: false,
        GameName: "",
        Backend: "Unknown",
        Architecture: "unknown",
        BepInExInstalled: false,
        BepInExVersion: null,
        RecommendedRuntime: "Unknown",
        PluginInstalled: false,
        PluginDirectory: "",
        ConfigDirectory: "",
        ProtectedDataPaths: []
      } as GameInspection as T;
    }

    const gameName = gameRoot.split(/[\\/]/).filter(Boolean).pop() || "Unity 游戏";
    return {
      GameRoot: gameRoot,
      DirectoryExists: true,
      IsValidUnityGame: true,
      GameName: gameName,
      Backend: "IL2CPP",
      Architecture: "x64",
      BepInExInstalled: true,
      BepInExVersion: "BepInEx 6",
      RecommendedRuntime: "IL2CPP",
      PluginInstalled: true,
      PluginDirectory: `${gameRoot}\\BepInEx\\plugins\\HUnityAutoTranslator`,
      ConfigDirectory: `${gameRoot}\\BepInEx\\config\\HUnityAutoTranslator`,
      ProtectedDataPaths: [
        `${gameRoot}\\BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite`,
        `${gameRoot}\\BepInEx\\config\\HUnityAutoTranslator\\providers`
      ]
    } as GameInspection as T;
  }

  if (command === "pickGameDirectory") {
    return "" as T;
  }

  if (command === "getWindowState") {
    return "normal" as T;
  }

  if (command === "windowMinimize" || command === "windowMaximizeRestore" || command === "windowClose") {
    return null as T;
  }

  if (command === "pickFontFile") {
    return { Status: "cancelled", FilePath: null, FontName: null, Message: "已取消选择字体文件。" } as T;
  }

  if (command === "createInstallPlan") {
    return buildFakePlan(payload) as T;
  }

  if (command === "executeInstallPlan") {
    const plan = buildFakePlan(payload);
    const runId = "dev-" + Date.now();
    runFakeInstall(runId, plan as InstallPlan);
    return { runId, plan } as T;
  }

  if (command === "cancelInstall") {
    const runId = readPayloadString(payload, "runId");
    window.setTimeout(() => devDispatch("installCancelled", { runId, operationIndex: -1, backupDirectory: null }), 50);
    return null as T;
  }

  if (command === "rollbackInstall") {
    return { Succeeded: true, RestoredPaths: [], Errors: [] } as RollbackResult as T;
  }

  if (command === "getEmbeddedBundleInfo") {
    return [
      { Key: "bepinex5-framework",       Kind: "BepInExFramework", Runtime: "BepInEx5Mono", Backend: "None",    Version: "5.4.23.5",     Sha256: "dev", SizeBytes: 639118 },
      { Key: "bepinex6mono-framework",   Kind: "BepInExFramework", Runtime: "Mono",         Backend: "None",    Version: "6.0.0-pre.2",  Sha256: "dev", SizeBytes: 645915 },
      { Key: "bepinex6il2cpp-framework", Kind: "BepInExFramework", Runtime: "IL2CPP",       Backend: "None",    Version: "6.0.0-pre.2",  Sha256: "dev", SizeBytes: 34146254 },
      { Key: "plugin-bepinex5",          Kind: "PluginPackage",    Runtime: "BepInEx5Mono", Backend: "None",    Version: "0.1.1",        Sha256: "dev", SizeBytes: 1_200_000 },
      { Key: "plugin-mono",              Kind: "PluginPackage",    Runtime: "Mono",         Backend: "None",    Version: "0.1.1",        Sha256: "dev", SizeBytes: 1_200_000 },
      { Key: "plugin-il2cpp",            Kind: "PluginPackage",    Runtime: "IL2CPP",       Backend: "None",    Version: "0.1.1",        Sha256: "dev", SizeBytes: 3_200_000 },
      { Key: "llamacpp-cuda13",          Kind: "LlamaCppBackend",  Runtime: "Unknown",      Backend: "Cuda13",  Version: "0.1.1",        Sha256: "dev", SizeBytes: 470_000_000 },
      { Key: "llamacpp-vulkan",          Kind: "LlamaCppBackend",  Runtime: "Unknown",      Backend: "Vulkan",  Version: "0.1.1",        Sha256: "dev", SizeBytes: 78_000_000 }
    ] as EmbeddedAssetInfo[] as T;
  }

  if (command === "pickFile") {
    return { Status: "cancelled", FilePath: null } as T;
  }

  if (command === "loadPluginConfig" || command === "savePluginConfig") {
    const gameRoot = readPayloadString(payload, "gameRoot");
    const config = command === "savePluginConfig" && payload && typeof payload === "object" && "config" in payload
      ? (payload as { config?: PluginConfigPayload["Config"] }).config ?? {}
      : {};
    return {
      SettingsPath: `${gameRoot}\\BepInEx\\config\\com.hanfeng.hunityautotranslator.cfg`,
      Config: config,
      ProviderDirectory: `${gameRoot}\\BepInEx\\config\\HUnityAutoTranslator\\providers`,
      TextureImageProviderDirectory: `${gameRoot}\\BepInEx\\config\\HUnityAutoTranslator\\texture-image-providers`
    } as PluginConfigPayload as T;
  }

  if (command === "queryTranslations") {
    return {
      TotalCount: 2,
      Items: [
        sampleRow("Continue", "继续", "MainMenu"),
        sampleRow("Options", "选项", "MainMenu")
      ]
    } as TranslationCachePage as T;
  }

  if (command === "getTranslationFilterOptions") {
    return {
      Column: readPayloadString(payload, "column"),
      Items: [
        { Value: "MainMenu", Count: 2 },
        { Value: null, Count: 1 }
      ]
    } as TranslationCacheFilterOptionPage as T;
  }

  if (command === "updateTranslation") {
    return readPayloadObject(payload, "entry") as T;
  }

  if (command === "deleteTranslations") {
    const entries = readPayloadArray(payload, "entries");
    return { DeletedCount: entries.length } as DeleteResult as T;
  }

  if (command === "exportTranslations") {
    return "SourceText,TranslatedText\nContinue,继续\nOptions,选项\n" as T;
  }

  if (command === "importTranslations") {
    return { ImportedCount: 0, Errors: [] } as TranslationCacheImportResult as T;
  }

  return {} as T;
}

function sampleRow(source: string, translated: string, scene: string) {
  const now = new Date().toISOString();
  return {
    SourceText: source,
    TargetLanguage: "zh-Hans",
    ProviderKind: "OpenAI",
    ProviderBaseUrl: "https://api.openai.com",
    ProviderEndpoint: "/v1/responses",
    ProviderModel: "gpt-5.4-mini",
    PromptPolicyVersion: "prompt-v4",
    TranslatedText: translated,
    SceneName: scene,
    ComponentHierarchy: "Canvas/Menu",
    ComponentType: "TMP_Text",
    ReplacementFont: null,
    CreatedUtc: now,
    UpdatedUtc: now
  };
}

function readPayloadString(payload: unknown, key: string): string {
  return payload && typeof payload === "object" && key in payload
    ? String((payload as Record<string, unknown>)[key] ?? "")
    : "";
}

function readPayloadBool(payload: unknown, key: string): boolean {
  return payload && typeof payload === "object" && key in payload
    ? (payload as Record<string, unknown>)[key] === true
    : false;
}

function readPayloadObject(payload: unknown, key: string): unknown {
  return payload && typeof payload === "object" && key in payload
    ? (payload as Record<string, unknown>)[key]
    : {};
}

function readPayloadArray(payload: unknown, key: string): unknown[] {
  const value = readPayloadObject(payload, key);
  return Array.isArray(value) ? value : [];
}

function buildFakePlan(payload: unknown): InstallPlan {
  const gameRoot = readPayloadString(payload, "gameRoot");
  const inspection = fallbackResponse<GameInspection>("inspectGame", { gameRoot });
  const includeLlama = readPayloadBool(payload, "includeLlamaCppBackend");
  const mode = readPayloadString(payload, "mode") || "Full";
  const operations: InstallPlan["Operations"] = [
    { Kind: "CreateDirectory", SourcePath: "", DestinationPath: `${gameRoot}\\BepInEx\\plugins\\HUnityAutoTranslator`, Description: "确保插件目录存在", SourceKind: "None" },
    { Kind: "ExtractPackage", SourcePath: "bepinex6il2cpp-framework", DestinationPath: gameRoot, Description: "安装 BepInEx 框架(6.0.0-pre.2)", SourceKind: "EmbeddedAsset" },
    { Kind: "PrepareUnityBaseLibraries", SourcePath: "2022.3.21f1", DestinationPath: `${gameRoot}\\BepInEx\\unity-libs\\2022.3.21f1.zip`, Description: "准备 Unity 2022.3.21f1 基础库(全局缓存或一次性下载)", SourceKind: "None" },
    { Kind: "ExtractPackage", SourcePath: "plugin-il2cpp", DestinationPath: gameRoot, Description: "解压 HUnityAutoTranslator 插件包(0.1.1)", SourceKind: "EmbeddedAsset" }
  ];
  if (includeLlama) {
    operations.push({ Kind: "ExtractPackage", SourcePath: "llamacpp-cuda13", DestinationPath: gameRoot, Description: "解压 llama.cpp 后端包(Cuda13)", SourceKind: "EmbeddedAsset" });
  }
  operations.push({ Kind: "VerifyFile", SourcePath: "", DestinationPath: `${gameRoot}\\BepInEx\\plugins\\HUnityAutoTranslator\\HUnityAutoTranslator.Plugin.IL2CPP.dll`, Description: "验证插件 DLL", SourceKind: "None" });
  return {
    Inspection: inspection,
    Mode: mode,
    PluginPackageName: "HUnityAutoTranslator-0.1.1-il2cpp.zip",
    LlamaCppPackageName: includeLlama ? "HUnityAutoTranslator-0.1.1-llamacpp-cuda13.zip" : null,
    ProtectedPaths: inspection.ProtectedDataPaths,
    Operations: operations,
    BackupDirectory: `${gameRoot}\\BepInEx\\config\\HUnityAutoTranslator\\toolbox-backups\\dev`,
    IsDryRun: readPayloadBool(payload, "dryRun")
  };
}

function runFakeInstall(runId: string, plan: InstallPlan): void {
  const operations = plan.Operations;
  let cancelled = false;
  const onCancel = (event: Event): void => {
    const detail = (event as CustomEvent).detail as { runId?: string };
    if (detail?.runId === runId) cancelled = true;
  };
  devEventBus.addEventListener("installCancelled", onCancel as EventListener);

  operations.forEach((op, index) => {
    window.setTimeout(() => {
      if (cancelled) return;
      const percent = (index + 1) / operations.length;
      const stage = guessStageFromOp(op.Kind, op.Description);
      devDispatch("installProgress", {
        runId,
        operationIndex: index + 1,
        operationCount: operations.length,
        stage,
        message: op.Description,
        percent,
        currentDestination: op.DestinationPath
      });
    }, 400 * (index + 1));
  });

  window.setTimeout(() => {
    if (cancelled) return;
    devDispatch("installCompleted", {
      runId,
      result: {
        Succeeded: true,
        Message: plan.IsDryRun ? "干跑成功" : "安装成功(模拟)",
        BackupDirectory: plan.BackupDirectory,
        WrittenPaths: [],
        Errors: [],
        SkippedProtectedPaths: [],
        FinalStage: "Completed",
        FailedOperationIndex: -1
      }
    });
    devEventBus.removeEventListener("installCancelled", onCancel as EventListener);
  }, 400 * (operations.length + 1));
}

function guessStageFromOp(kind: string, description: string): string {
  if (kind === "BackupExisting") return "Backup";
  if (kind === "VerifyFile") return "Verify";
  if (kind === "PrepareUnityBaseLibraries") return "PrepareUnityLibs";
  if (kind === "CreateDirectory" || kind === "PreserveUserData") return "Preparing";
  if (kind === "ExtractPackage") {
    if (/bepinex/i.test(description)) return "ExtractFramework";
    if (/llama/i.test(description)) return "ExtractLlamaCpp";
    return "ExtractPlugin";
  }
  return "Preparing";
}
