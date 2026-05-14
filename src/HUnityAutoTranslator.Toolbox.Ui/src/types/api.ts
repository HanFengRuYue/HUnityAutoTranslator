export type PageKey = "library" | "install" | "config" | "translations" | "about";
export type ThemeMode = "system" | "light" | "dark";
export type LibraryLayout = "grid" | "list";
export type LibraryPosterSize = "compact" | "normal" | "large";
export type LibraryAccent = "blue" | "green" | "amber" | "rose";
export type InstallMode = "Full" | "PluginOnly" | "LlamaCppBackendOnly";
export type ToastKind = "info" | "ok" | "warn" | "error";

export interface Toast {
  id: number;
  kind: ToastKind;
  message: string;
}

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

export interface GameLibraryEntry {
  Id: string;
  Root: string;
  Name: string;
  AddedUtc: string;
  UpdatedUtc: string;
  Inspection: GameInspection | null;
}

export type InstallStage =
  | "Preparing"
  | "Backup"
  | "ExtractFramework"
  | "ExtractPlugin"
  | "ExtractLlamaCpp"
  | "Verify"
  | "Completed"
  | "Failed"
  | "Cancelled"
  | "Rollback"
  | "PrepareUnityLibs";

export type InstallOperationSourceKind = "None" | "EmbeddedAsset" | "LocalFile" | "Directory";

export type BepInExHandling = "Auto" | "Always" | "Skip";

export type BackupPolicy = "Auto" | "Always" | "Skip";

export type EmbeddedAssetKind = "BepInExFramework" | "PluginPackage" | "LlamaCppBackend";

export interface InstallOperation {
  Kind: string;
  SourcePath: string;
  DestinationPath: string;
  Description: string;
  SourceKind?: InstallOperationSourceKind;
}

export interface InstallPlan {
  Inspection: GameInspection;
  Mode: InstallMode | string;
  PluginPackageName: string;
  LlamaCppPackageName: string | null;
  ProtectedPaths: string[];
  Operations: InstallOperation[];
  BackupDirectory: string;
  IsDryRun?: boolean;
}

export interface InstallResult {
  Succeeded: boolean;
  Message: string;
  BackupDirectory: string;
  WrittenPaths: string[];
  Errors: string[];
  SkippedProtectedPaths: string[] | null;
  FinalStage: InstallStage;
  FailedOperationIndex: number;
}

export interface RollbackResult {
  Succeeded: boolean;
  RestoredPaths: string[];
  Errors: string[];
}

export interface EmbeddedAssetInfo {
  Key: string;
  Kind: EmbeddedAssetKind;
  Runtime: string;
  Backend: string;
  Version: string;
  Sha256: string;
  SizeBytes: number;
}

export interface InstallProgressPayload {
  type: "installProgress";
  runId: string;
  operationIndex: number;
  operationCount: number;
  stage: InstallStage;
  message: string;
  percent: number;
  currentDestination: string | null;
}

export interface InstallCompletedPayload {
  type: "installCompleted";
  runId: string;
  result: InstallResult;
}

export interface InstallFailedPayload {
  type: "installFailed";
  runId: string;
  error: string;
  stage: InstallStage;
  operationIndex: number;
  backupDirectory: string | null;
  errors?: string[];
}

export interface InstallCancelledPayload {
  type: "installCancelled";
  runId: string;
  operationIndex: number;
  backupDirectory: string | null;
}

export type InstallRunStatus = "running" | "succeeded" | "failed" | "cancelled" | "rollback";

export interface InstallRunErrorState {
  message: string;
  stage: InstallStage;
  operationIndex: number;
  backupDirectory: string | null;
}

export interface InstallRunState {
  id: string;
  plan: InstallPlan;
  status: InstallRunStatus;
  progress: {
    operationIndex: number;
    operationCount: number;
    stage: InstallStage;
    message: string;
    percent: number;
    currentDestination?: string | null;
  };
  perStepStatus: Array<"pending" | "running" | "done" | "failed" | "skipped">;
  result: InstallResult | null;
  error: InstallRunErrorState | null;
  startedAt: string;
}

export interface CustomInstallOptions {
  mode: InstallMode;
  includeLlamaCppBackend: boolean;
  llamaCppBackend: "Cuda13" | "Vulkan";
  runtimeOverride: "" | "BepInEx5Mono" | "Mono" | "IL2CPP";
  bepInExHandling: BepInExHandling;
  backupPolicy: BackupPolicy;
  customPluginDirectory: string;
  customBackupDirectory: string;
  customPluginZipPath: string;
  customBepInExZipPath: string;
  customLlamaCppZipPath: string;
  customUnityLibraryZipPath: string;
  unityVersionOverride: string;
  dryRun: boolean;
  forceReinstall: boolean;
  skipPostInstallVerification: boolean;
}

export interface FilePickResult {
  Status: string;
  FilePath: string | null;
}

export interface DatabaseMaintenanceResult {
  DatabasePath: string;
  BackupPath: string | null;
  IntegrityOk: boolean;
  Actions: string[];
  Errors: string[];
}

export interface TranslationQualityConfig {
  Enabled: boolean;
  Mode: string;
  AllowAlreadyTargetLanguageSource: boolean;
  EnableRepair: boolean;
  MaxRetryCount: number;
  RejectGeneratedOuterSymbols: boolean;
  RejectUntranslatedLatinUiText: boolean;
  RejectShortSettingValue: boolean;
  RejectLiteralStateTranslation: boolean;
  RejectSameParentOptionCollision: boolean;
  ShortSettingValueMinSourceLength: number;
  ShortSettingValueMaxTranslationTextElements: number;
}

export interface PromptTemplateConfig {
  SystemPrompt: string | null;
  GlossarySystemPolicy: string | null;
  BatchUserPrompt: string | null;
  GlossaryTermsSection: string | null;
  CurrentItemContextSection: string | null;
  ItemHintsSection: string | null;
  ContextExamplesSection: string | null;
  GlossaryRepairPrompt: string | null;
  QualityRepairPrompt: string | null;
  GlossaryExtractionSystemPrompt: string | null;
  GlossaryExtractionUserPrompt: string | null;
}

export interface LlamaCppConfig {
  ModelPath: string | null;
  ContextSize: number;
  GpuLayers: number;
  ParallelSlots: number;
  BatchSize: number;
  UBatchSize: number;
  FlashAttentionMode: string;
  AutoStartOnStartup: boolean;
  CacheReuseTokens: number;
}

export interface TextureImageTranslationConfig {
  Enabled: boolean;
  BaseUrl: string;
  EditEndpoint: string;
  VisionEndpoint: string;
  ImageModel: string;
  VisionModel: string;
  Quality: string;
  TimeoutSeconds: number;
  MaxConcurrentRequests: number;
  EnableVisionConfirmation: boolean;
}

export interface UpdateConfigRequest {
  TargetLanguage?: string | null;
  GameTitle?: string | null;
  MaxConcurrentRequests?: number | null;
  RequestsPerMinute?: number | null;
  Enabled?: boolean | null;
  AutoOpenControlPanel?: boolean | null;
  HttpPort?: number | null;
  OpenControlPanelHotkey?: string | null;
  ToggleTranslationHotkey?: string | null;
  ForceScanHotkey?: string | null;
  ToggleFontHotkey?: string | null;
  ProviderKind?: string | number | null;
  BaseUrl?: string | null;
  Endpoint?: string | null;
  Model?: string | null;
  Style?: string | number | null;
  MaxBatchCharacters?: number | null;
  ScanIntervalMilliseconds?: number | null;
  MaxScanTargetsPerTick?: number | null;
  MaxWritebacksPerFrame?: number | null;
  RequestTimeoutSeconds?: number | null;
  ReasoningEffort?: string | null;
  OutputVerbosity?: string | null;
  DeepSeekThinkingMode?: string | null;
  OpenAICompatibleCustomHeaders?: string | null;
  OpenAICompatibleExtraBodyJson?: string | null;
  Temperature?: number | null;
  ClearTemperature?: boolean | null;
  CustomPrompt?: string | null;
  PromptTemplates?: PromptTemplateConfig | null;
  TranslationQuality?: TranslationQualityConfig | null;
  MaxSourceTextLength?: number | null;
  IgnoreInvisibleText?: boolean | null;
  SkipNumericSymbolText?: boolean | null;
  EnableCacheLookup?: boolean | null;
  EnableTranslationDebugLogs?: boolean | null;
  EnableTranslationContext?: boolean | null;
  TranslationContextMaxExamples?: number | null;
  TranslationContextMaxCharacters?: number | null;
  EnableGlossary?: boolean | null;
  EnableAutoTermExtraction?: boolean | null;
  GlossaryMaxTerms?: number | null;
  GlossaryMaxCharacters?: number | null;
  ManualEditsOverrideAi?: boolean | null;
  ReapplyRememberedTranslations?: boolean | null;
  EnableUgui?: boolean | null;
  EnableTmp?: boolean | null;
  EnableImgui?: boolean | null;
  EnableFontReplacement?: boolean | null;
  ReplaceUguiFonts?: boolean | null;
  ReplaceTmpFonts?: boolean | null;
  ReplaceImguiFonts?: boolean | null;
  AutoUseCjkFallbackFonts?: boolean | null;
  ReplacementFontName?: string | null;
  ReplacementFontFile?: string | null;
  FontSamplingPointSize?: number | null;
  FontSizeAdjustmentMode?: string | number | null;
  FontSizeAdjustmentValue?: number | null;
  EnableTmpNativeAutoSize?: boolean | null;
  TextureImageTranslation?: TextureImageTranslationConfig | null;
  LlamaCpp?: LlamaCppConfig | null;
}

export interface PluginConfigPayload {
  SettingsPath: string;
  Config: UpdateConfigRequest;
  ProviderDirectory: string;
  TextureImageProviderDirectory: string;
}

export interface FontPickResult {
  Status: string;
  FilePath: string | null;
  FontName: string | null;
  Message: string;
}

export interface AppInfo {
  Name: string;
  Version: string;
}

export interface TranslationCacheEntry {
  SourceText: string;
  TargetLanguage: string;
  ProviderKind: string;
  ProviderBaseUrl: string;
  ProviderEndpoint: string;
  ProviderModel: string;
  PromptPolicyVersion: string;
  TranslatedText: string | null;
  SceneName: string | null;
  ComponentHierarchy: string | null;
  ComponentType: string | null;
  ReplacementFont: string | null;
  CreatedUtc: string;
  UpdatedUtc: string;
}

export interface TranslationCachePage {
  TotalCount: number;
  Items: TranslationCacheEntry[];
}

export interface TranslationCacheFilterOption {
  Value: string | null;
  Count: number;
}

export interface TranslationCacheFilterOptionPage {
  Column: string;
  Items: TranslationCacheFilterOption[];
}

export interface TranslationCacheImportResult {
  ImportedCount: number;
  Errors: string[];
}

export interface DeleteResult {
  DeletedCount: number;
}
