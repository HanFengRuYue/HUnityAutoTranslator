export type PageKey = "install" | "config" | "translations" | "history" | "about";
export type ThemeMode = "system" | "light" | "dark";

export interface GameInspection {
  GameRoot: string;
  DirectoryExists: boolean;
  IsValidUnityGame: boolean;
  GameName: string;
  Backend: string;
  Architecture: string;
  BepInExInstalled: boolean;
  BepInExVersion: string | null;
  RecommendedRuntime: string;
  PluginInstalled: boolean;
  PluginDirectory: string;
  ConfigDirectory: string;
  ProtectedDataPaths: string[];
}

export interface InstallOperation {
  Kind: string;
  SourcePath: string;
  DestinationPath: string;
  Description: string;
}

export interface InstallPlan {
  Mode: string;
  PluginPackageName: string;
  LlamaCppPackageName: string | null;
  ProtectedPaths: string[];
  Operations: InstallOperation[];
  BackupDirectory: string;
}

export interface DatabaseMaintenanceResult {
  DatabasePath: string;
  BackupPath: string | null;
  IntegrityOk: boolean;
  Actions: string[];
  Errors: string[];
}
