interface NativeBridge {
  invoke<T>(command: string, payload?: unknown): Promise<T>;
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
  if (command === "inspectGame") {
    const gameRoot = readPayloadString(payload, "gameRoot") || "D:\\Game\\The Glitched Attraction";
    return {
      GameRoot: gameRoot,
      DirectoryExists: true,
      IsValidUnityGame: true,
      GameName: "The Glitched Attraction",
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
    } as T;
  }

  if (command === "createInstallPlan") {
    return {
      Mode: "Full",
      PluginPackageName: "HUnityAutoTranslator-0.1.1-il2cpp.zip",
      LlamaCppPackageName: null,
      ProtectedPaths: [
        "BepInEx\\config\\HUnityAutoTranslator\\translation-cache.sqlite",
        "BepInEx\\config\\HUnityAutoTranslator\\providers"
      ],
      BackupDirectory: "BepInEx\\config\\HUnityAutoTranslator\\toolbox-backups\\preview",
      Operations: [
        { Kind: "CreateDirectory", SourcePath: "", DestinationPath: "BepInEx\\plugins\\HUnityAutoTranslator", Description: "确保插件目录存在" },
        { Kind: "ExtractPackage", SourcePath: "HUnityAutoTranslator-0.1.1-il2cpp.zip", DestinationPath: readPayloadString(payload, "gameRoot"), Description: "解压插件包" },
        { Kind: "VerifyFile", SourcePath: "", DestinationPath: "HUnityAutoTranslator.Plugin.IL2CPP.dll", Description: "验证插件 DLL" }
      ]
    } as T;
  }

  if (command === "getAppInfo") {
    return { Name: "HUnityAutoTranslator 工具箱", Version: "0.1.1" } as T;
  }

  if (command === "runDatabaseMaintenance") {
    return {
      DatabasePath: readPayloadString(payload, "databasePath"),
      BackupPath: "toolbox-backups\\translation-cache-preview.sqlite",
      IntegrityOk: true,
      Actions: ["BACKUP", "PRAGMA integrity_check", "REINDEX"],
      Errors: []
    } as T;
  }

  return {} as T;
}

function readPayloadString(payload: unknown, key: string): string {
  return payload && typeof payload === "object" && key in payload
    ? String((payload as Record<string, unknown>)[key] ?? "")
    : "";
}
