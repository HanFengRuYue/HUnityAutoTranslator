import type {
  DeleteResult,
  GameInspection,
  PluginConfigPayload,
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
    const gameRoot = readPayloadString(payload, "gameRoot");
    return {
      Inspection: fallbackResponse<GameInspection>("inspectGame", { gameRoot }),
      Mode: readPayloadString(payload, "mode") || "Full",
      PluginPackageName: "HUnityAutoTranslator-0.1.1-il2cpp.zip",
      LlamaCppPackageName: readPayloadBool(payload, "includeLlamaCppBackend")
        ? "HUnityAutoTranslator-0.1.1-llamacpp-cuda13.zip"
        : null,
      ProtectedPaths: [
        "BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite",
        "BepInEx\\config\\HUnityAutoTranslator\\providers"
      ],
      BackupDirectory: "BepInEx\\config\\HUnityAutoTranslator\\toolbox-backups\\preview",
      Operations: [
        { Kind: "CreateDirectory", SourcePath: "", DestinationPath: "BepInEx\\plugins\\HUnityAutoTranslator", Description: "确保插件目录存在" },
        { Kind: "ExtractPackage", SourcePath: "HUnityAutoTranslator-0.1.1-il2cpp.zip", DestinationPath: gameRoot, Description: "解压插件包" },
        { Kind: "VerifyFile", SourcePath: "", DestinationPath: "HUnityAutoTranslator.Plugin.IL2CPP.dll", Description: "验证插件 DLL" }
      ]
    } as T;
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
