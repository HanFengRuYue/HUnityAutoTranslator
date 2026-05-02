export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };

export type PageKey =
  | "status"
  | "plugin"
  | "ai"
  | "glossary"
  | "textures"
  | "editor"
  | "about";

export type ThemeMode = "system" | "light" | "dark";
export type ConnectionState = "connecting" | "online" | "offline";
export type ToastKind = "ok" | "warn" | "error" | "info";
export type FontPickStatus = "selected" | "cancelled" | "unsupported" | "error";
export type SelfCheckSeverity = "Ok" | "Info" | "Warning" | "Error" | "Skipped" | number;
export type SelfCheckRunState = "NotStarted" | "Running" | "Completed" | "Failed" | number;

export interface SelfCheckItem {
  Id: string;
  Category: string;
  Name: string;
  Severity: SelfCheckSeverity;
  Summary: string;
  Evidence: string;
  Recommendation: string;
  DurationMilliseconds: number;
}

export interface SelfCheckReport {
  State: SelfCheckRunState;
  Severity: SelfCheckSeverity;
  StartedUtc: string | null;
  CompletedUtc: string | null;
  DurationMilliseconds: number;
  ItemCount: number;
  OkCount: number;
  InfoCount: number;
  WarningCount: number;
  ErrorCount: number;
  SkippedCount: number;
  Items: SelfCheckItem[];
  Message: string;
}

export interface ProviderStatus {
  State: string;
  Message: string;
  CheckedUtc: string | null;
}

export interface ProviderProfileState {
  Id: string;
  Name: string;
  Enabled: boolean;
  Priority: number;
  Kind: number | string;
  BaseUrl: string;
  Endpoint: string;
  Model: string;
  ApiKeyConfigured: boolean;
  ApiKeyPreview: string | null;
  MaxConcurrentRequests: number;
  RequestsPerMinute: number;
  RequestTimeoutSeconds: number;
  ReasoningEffort: string;
  OutputVerbosity: string;
  DeepSeekThinkingMode: string;
  OpenAICompatibleCustomHeaders: string | null;
  OpenAICompatibleExtraBodyJson: string | null;
  LlamaCpp: LlamaCppConfig | null;
  Temperature: number | null;
  IsActive: boolean;
  ConsecutiveFailureCount: number;
  CooldownRemainingSeconds: number;
  LastError: string | null;
}

export interface ProviderProfileUpdateRequest {
  Id?: string;
  Name?: string | null;
  Enabled?: boolean;
  Priority?: number;
  Kind?: number;
  BaseUrl?: string;
  Endpoint?: string;
  Model?: string;
  ApiKey?: string | null;
  ClearApiKey?: boolean;
  MaxConcurrentRequests?: number;
  RequestsPerMinute?: number;
  RequestTimeoutSeconds?: number;
  ReasoningEffort?: string;
  OutputVerbosity?: string;
  DeepSeekThinkingMode?: string;
  OpenAICompatibleCustomHeaders?: string | null;
  OpenAICompatibleExtraBodyJson?: string | null;
  LlamaCpp?: LlamaCppConfig | null;
  Temperature?: number | null;
  ClearTemperature?: boolean;
}

export interface ProviderProfileImportResult {
  Succeeded: boolean;
  Message: string;
  Profile: ProviderProfileState | null;
}

export interface TextureImageProviderProfileState {
  Id: string;
  Name: string;
  Enabled: boolean;
  Priority: number;
  BaseUrl: string;
  EditEndpoint: string;
  VisionEndpoint: string;
  ImageModel: string;
  VisionModel: string;
  Quality: string;
  TimeoutSeconds: number;
  MaxConcurrentRequests: number;
  EnableVisionConfirmation: boolean;
  ApiKeyConfigured: boolean;
  ApiKeyPreview: string | null;
}

export interface TextureImageProviderProfileUpdateRequest {
  Id?: string;
  Name?: string | null;
  Enabled?: boolean;
  Priority?: number;
  BaseUrl?: string;
  EditEndpoint?: string;
  VisionEndpoint?: string;
  ImageModel?: string;
  VisionModel?: string;
  Quality?: string;
  TimeoutSeconds?: number;
  MaxConcurrentRequests?: number;
  EnableVisionConfirmation?: boolean;
  ApiKey?: string | null;
  ClearApiKey?: boolean;
}

export interface TextureImageProviderProfileImportResult {
  Succeeded: boolean;
  Message: string;
  Profile: TextureImageProviderProfileState | null;
}

export interface FontPickResult {
  Status: FontPickStatus;
  FilePath: string | null;
  FontName: string | null;
  Message: string;
}

export interface FontPickOptions {
  CopyToConfig?: boolean;
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

export interface LlamaCppServerStatus {
  State: string;
  Backend: string;
  ModelPath: string | null;
  Port: number;
  Message: string;
  LastOutput: string | null;
  Installed: boolean;
  Release: string | null;
  Variant: string | null;
  ServerPath: string | null;
}

export interface LlamaCppModelPickResult {
  Status: "selected" | "cancelled" | "unsupported" | "error";
  FilePath: string | null;
  Message: string;
}

export interface LlamaCppModelDownloadPreset {
  Id: string;
  Label: string;
  ModelScopeModelId: string;
  FileName: string;
  FileSizeBytes: number;
  Sha256: string;
  Quantization: string;
  UseCase: string;
  License: string;
  Notes: string;
  DownloadUrl: string;
}

export interface LlamaCppModelDownloadRequest {
  PresetId: string;
}

export interface LlamaCppModelDownloadStatus {
  State: string;
  PresetId: string | null;
  PresetLabel: string | null;
  FileName: string | null;
  LocalPath: string | null;
  DownloadedBytes: number;
  TotalBytes: number;
  ProgressPercent: number;
  Message: string;
  Error: string | null;
  StartedUtc: string | null;
  CompletedUtc: string | null;
}

export interface LlamaCppBenchmarkCandidate {
  Tool: string;
  BatchSize: number;
  UBatchSize: number;
  FlashAttentionMode: string;
  ParallelSlots: number;
  TotalContextSize: number;
  PromptTokensPerSecond: number | null;
  GenerationTokensPerSecond: number | null;
  TotalTokensPerSecond: number | null;
  TotalSeconds: number | null;
  Succeeded: boolean;
  Error: string | null;
}

export interface LlamaCppBenchmarkResult {
  Succeeded: boolean;
  Saved: boolean;
  Message: string;
  CurrentConfig: LlamaCppConfig;
  RecommendedConfig: LlamaCppConfig | null;
  Candidates: LlamaCppBenchmarkCandidate[];
  Errors: string[];
  LastOutput: string | null;
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

export interface RecentTranslationPreview {
  SourceText: string;
  TranslatedText: string;
  TargetLanguage: string;
  Provider: string;
  Model: string;
  Context: string | null;
  CompletedUtc: string;
  ProviderProfileId: string | null;
  ProviderProfileName: string | null;
  ProviderProfileKind: string | null;
}

export interface ProviderActivityPreview {
  Id: string;
  Name: string;
  Kind: string | number;
  Model: string;
  StartedUtc: string;
}

export interface ControlPanelState {
  Enabled: boolean;
  TargetLanguage: string;
  GameTitle: string | null;
  AutomaticGameTitle: string | null;
  Style: number | string;
  ProviderKind: number | string;
  BaseUrl: string;
  Endpoint: string;
  Model: string;
  ApiKeyConfigured: boolean;
  ApiKeyPreview: string | null;
  AutoOpenControlPanel: boolean;
  OpenControlPanelHotkey: string;
  ToggleTranslationHotkey: string;
  ForceScanHotkey: string;
  ToggleFontHotkey: string;
  QueueCount: number;
  CacheCount: number;
  CapturedTextCount: number;
  QueuedTextCount: number;
  InFlightTranslationCount: number;
  CompletedTranslationCount: number;
  TotalTokenCount: number;
  AverageTranslationMilliseconds: number;
  AverageCharactersPerSecond: number;
  WritebackQueueCount: number;
  ProviderStatus: ProviderStatus;
  RecentTranslations: RecentTranslationPreview[];
  MaxConcurrentRequests: number;
  EffectiveMaxConcurrentRequests: number;
  RequestsPerMinute: number;
  MaxBatchCharacters: number;
  ScanIntervalMilliseconds: number;
  MaxScanTargetsPerTick: number;
  MaxWritebacksPerFrame: number;
  RequestTimeoutSeconds: number;
  ReasoningEffort: string;
  OutputVerbosity: string;
  DeepSeekThinkingMode: string;
  OpenAICompatibleCustomHeaders: string | null;
  OpenAICompatibleExtraBodyJson: string | null;
  Temperature: number | null;
  CustomPrompt: string | null;
  DefaultSystemPrompt: string;
  PromptTemplates: PromptTemplateConfig;
  DefaultPromptTemplates: PromptTemplateConfig;
  MaxSourceTextLength: number;
  IgnoreInvisibleText: boolean;
  SkipNumericSymbolText: boolean;
  EnableCacheLookup: boolean;
  EnableTranslationDebugLogs: boolean;
  EnableTranslationContext: boolean;
  TranslationContextMaxExamples: number;
  TranslationContextMaxCharacters: number;
  EnableGlossary: boolean;
  EnableAutoTermExtraction: boolean;
  GlossaryMaxTerms: number;
  GlossaryMaxCharacters: number;
  ManualEditsOverrideAi: boolean;
  ReapplyRememberedTranslations: boolean;
  EnableUgui: boolean;
  EnableTmp: boolean;
  EnableImgui: boolean;
  EnableFontReplacement: boolean;
  ReplaceUguiFonts: boolean;
  ReplaceTmpFonts: boolean;
  ReplaceImguiFonts: boolean;
  AutoUseCjkFallbackFonts: boolean;
  ReplacementFontName: string | null;
  ReplacementFontFile: string | null;
  AutomaticReplacementFontName: string | null;
  AutomaticReplacementFontFile: string | null;
  FontSamplingPointSize: number;
  FontSizeAdjustmentMode: number | string;
  FontSizeAdjustmentValue: number;
  LastError: string | null;
  TextureImageTranslation: TextureImageTranslationConfig;
  TextureImageApiKeyConfigured: boolean;
  LlamaCpp: LlamaCppConfig;
  LlamaCppStatus: LlamaCppServerStatus;
  ProviderProfiles: ProviderProfileState[] | null;
  ActiveProviderProfileId: string | null;
  ActiveProviderProfileName: string | null;
  ActiveProviderProfileKind: string | number | null;
  ActiveProviderProfileModel: string | null;
  ActiveTranslationProvider: ProviderActivityPreview | null;
  TextureImageProviderProfiles: TextureImageProviderProfileState[] | null;
  ActiveTextureImageProviderProfileId: string | null;
  ActiveTextureImageProviderProfileName: string | null;
  ActiveTextureImageProviderProfileModel: string | null;
  SelfCheck: SelfCheckReport | null;
}

export interface UpdateConfigRequest {
  TargetLanguage?: string;
  GameTitle?: string | null;
  MaxConcurrentRequests?: number;
  RequestsPerMinute?: number;
  Enabled?: boolean;
  AutoOpenControlPanel?: boolean;
  HttpPort?: number;
  OpenControlPanelHotkey?: string;
  ToggleTranslationHotkey?: string;
  ForceScanHotkey?: string;
  ToggleFontHotkey?: string;
  ProviderKind?: number;
  BaseUrl?: string;
  Endpoint?: string;
  Model?: string;
  Style?: number;
  MaxBatchCharacters?: number;
  ScanIntervalMilliseconds?: number;
  MaxScanTargetsPerTick?: number;
  MaxWritebacksPerFrame?: number;
  RequestTimeoutSeconds?: number;
  ReasoningEffort?: string;
  OutputVerbosity?: string;
  DeepSeekThinkingMode?: string;
  OpenAICompatibleCustomHeaders?: string | null;
  OpenAICompatibleExtraBodyJson?: string | null;
  Temperature?: number | null;
  ClearTemperature?: boolean;
  CustomPrompt?: string | null;
  PromptTemplates?: PromptTemplateConfig;
  MaxSourceTextLength?: number;
  IgnoreInvisibleText?: boolean;
  SkipNumericSymbolText?: boolean;
  EnableCacheLookup?: boolean;
  EnableTranslationDebugLogs?: boolean;
  EnableTranslationContext?: boolean;
  TranslationContextMaxExamples?: number;
  TranslationContextMaxCharacters?: number;
  EnableGlossary?: boolean;
  EnableAutoTermExtraction?: boolean;
  GlossaryMaxTerms?: number;
  GlossaryMaxCharacters?: number;
  ManualEditsOverrideAi?: boolean;
  ReapplyRememberedTranslations?: boolean;
  EnableUgui?: boolean;
  EnableTmp?: boolean;
  EnableImgui?: boolean;
  EnableFontReplacement?: boolean;
  ReplaceUguiFonts?: boolean;
  ReplaceTmpFonts?: boolean;
  ReplaceImguiFonts?: boolean;
  AutoUseCjkFallbackFonts?: boolean;
  ReplacementFontName?: string | null;
  ReplacementFontFile?: string | null;
  FontSamplingPointSize?: number;
  FontSizeAdjustmentMode?: number;
  FontSizeAdjustmentValue?: number;
  TextureImageTranslation?: TextureImageTranslationConfig;
  LlamaCpp?: LlamaCppConfig;
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

export interface TranslationCacheImportResult {
  ImportedCount: number;
  Errors: string[];
  RefreshQueuedCount?: number;
}

export interface TranslationCacheFilterOption {
  Value: string | null;
  Count: number;
}

export interface TranslationCacheFilterOptionPage {
  Column: string;
  Items: TranslationCacheFilterOption[];
}

export interface GlossaryTerm {
  SourceTerm: string;
  TargetTerm: string;
  TargetLanguage: string;
  NormalizedSourceTerm: string;
  Note: string | null;
  Enabled: boolean;
  Source: string;
  UsageCount: number;
  CreatedUtc: string;
  UpdatedUtc: string;
}

export interface GlossaryTermPage {
  TotalCount: number;
  Items: GlossaryTerm[];
}

export interface GlossaryTermRequest {
  SourceTerm: string;
  TargetTerm: string;
  TargetLanguage?: string;
  OriginalSourceTerm?: string | null;
  OriginalTargetLanguage?: string | null;
  Note?: string | null;
  Enabled?: boolean;
  UsageCount?: number;
}

export interface GlossaryFilterOption {
  Value: string | null;
  Count: number;
}

export interface GlossaryFilterOptionPage {
  Column: string;
  Items: GlossaryFilterOption[];
}

export interface ProviderModelInfo {
  Id: string;
  OwnedBy: string | null;
}

export interface ProviderBalanceInfo {
  Currency: string;
  TotalBalance: string;
  GrantedBalance: string | null;
  ToppedUpBalance: string | null;
}

export interface ProviderModelsResult {
  Succeeded: boolean;
  Message: string;
  Models: ProviderModelInfo[];
}

export interface ProviderBalanceResult {
  Succeeded: boolean;
  Message: string;
  Balances: ProviderBalanceInfo[];
}

export interface ProviderTestResult {
  Succeeded: boolean;
  Message: string;
}

export interface DeleteResult {
  DeletedCount: number;
}

export interface RetranslateResult {
  RequestedCount: number;
  QueuedCount: number;
}

export interface TranslationHighlightResult {
  Status: string;
  Message: string;
  TargetId: string | null;
}

export interface TextureReferenceInfo {
  TargetId: string;
  SceneName: string | null;
  ComponentHierarchy: string | null;
  ComponentType: string | null;
}

export interface TextureCatalogItem {
  SourceHash: string;
  TextureName: string;
  Width: number;
  Height: number;
  Format: string;
  FileName: string;
  ReferenceCount: number;
  References: TextureReferenceInfo[];
  HasOverride: boolean;
  OverrideUpdatedUtc: string | null;
  TextAnalysis: TextureTextAnalysis | null;
}

export type TextureTextStatus = number | string;

export interface TextureTextAnalysis {
  SourceHash: string;
  Status: TextureTextStatus;
  Confidence: number;
  DetectedText: string | null;
  Reason: string | null;
  NeedsManualReview: boolean;
  UserReviewed: boolean;
  UpdatedUtc: string;
  LastError: string | null;
}

export interface TextureCatalogScanStatus {
  IsScanning: boolean;
  Message: string;
  StartedUtc: string | null;
  CompletedUtc: string | null;
  ProcessedTargets: number;
  DiscoveredTextureCount: number;
  DiscoveredReferenceCount: number;
  DeferredTargetCount: number;
  DeferredTextureCount: number;
}

export interface TextureCatalogPage {
  ScannedUtc: string | null;
  TextureCount: number;
  ReferenceCount: number;
  OverrideCount: number;
  Items: TextureCatalogItem[];
  Errors: string[];
  TotalCount: number;
  FilteredCount: number;
  Offset: number;
  Limit: number;
  Scenes: string[];
  ScanStatus: TextureCatalogScanStatus;
}

export interface TextureScanResult {
  ScannedUtc: string;
  TextureCount: number;
  ReferenceCount: number;
  OverrideCount: number;
  Errors: string[];
  DeferredTargetCount: number;
  DeferredTextureCount: number;
  IsScanning: boolean;
  Message: string;
}

export interface TextureScanRequest {
  IncludeDeferredLargeTextures?: boolean;
}

export interface TextureImportResult {
  ImportedCount: number;
  AppliedCount: number;
  Errors: string[];
}

export interface TextureOverrideClearResult {
  DeletedCount: number;
  RestoredCount: number;
  Errors: string[];
}

export interface TextureTextDetectionResult {
  RequestedCount: number;
  UpdatedCount: number;
  Items: TextureTextAnalysis[];
  Errors: string[];
}

export interface TextureTextStatusUpdateResult {
  UpdatedCount: number;
  Items: TextureTextAnalysis[];
  Errors: string[];
}

export interface TextureImageTranslateResult {
  RequestedCount: number;
  GeneratedCount: number;
  AppliedCount: number;
  Items: TextureTextAnalysis[];
  Errors: string[];
}
