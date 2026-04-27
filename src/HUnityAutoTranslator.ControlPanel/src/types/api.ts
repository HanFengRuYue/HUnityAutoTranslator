export type JsonPrimitive = string | number | boolean | null;
export type JsonValue = JsonPrimitive | JsonValue[] | { [key: string]: JsonValue };

export type PageKey =
  | "status"
  | "plugin"
  | "ai"
  | "glossary"
  | "editor"
  | "about";

export type ThemeMode = "system" | "light" | "dark";
export type ConnectionState = "connecting" | "online" | "offline";
export type ToastKind = "ok" | "warn" | "error" | "info";
export type FontPickStatus = "selected" | "cancelled" | "unsupported" | "error";

export interface ProviderStatus {
  State: string;
  Message: string;
  CheckedUtc: string | null;
}

export interface FontPickResult {
  Status: FontPickStatus;
  FilePath: string | null;
  FontName: string | null;
  Message: string;
}

export interface LlamaCppConfig {
  ModelPath: string | null;
  ContextSize: number;
  GpuLayers: number;
  ParallelSlots: number;
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

export interface RecentTranslationPreview {
  SourceText: string;
  TranslatedText: string;
  TargetLanguage: string;
  Provider: string;
  Model: string;
  Context: string | null;
  CompletedUtc: string;
}

export interface ControlPanelState {
  Enabled: boolean;
  TargetLanguage: string;
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
  RequestsPerMinute: number;
  MaxBatchCharacters: number;
  ScanIntervalMilliseconds: number;
  MaxScanTargetsPerTick: number;
  MaxWritebacksPerFrame: number;
  RequestTimeoutSeconds: number;
  ReasoningEffort: string;
  OutputVerbosity: string;
  DeepSeekThinkingMode: string;
  Temperature: number | null;
  CustomPrompt: string | null;
  DefaultSystemPrompt: string;
  MaxSourceTextLength: number;
  IgnoreInvisibleText: boolean;
  SkipNumericSymbolText: boolean;
  EnableCacheLookup: boolean;
  EnableTranslationContext: boolean;
  TranslationContextMaxExamples: number;
  TranslationContextMaxCharacters: number;
  EnableGlossary: boolean;
  EnableAutoTermExtraction: boolean;
  GlossaryMaxTerms: number;
  GlossaryMaxCharacters: number;
  ManualEditsOverrideAi: boolean;
  ReapplyRememberedTranslations: boolean;
  CacheRetentionDays: number;
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
  LlamaCpp: LlamaCppConfig;
  LlamaCppStatus: LlamaCppServerStatus;
}

export interface UpdateConfigRequest {
  TargetLanguage?: string;
  MaxConcurrentRequests?: number;
  RequestsPerMinute?: number;
  Enabled?: boolean;
  AutoOpenControlPanel?: boolean;
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
  Temperature?: number | null;
  ClearTemperature?: boolean;
  CustomPrompt?: string | null;
  MaxSourceTextLength?: number;
  IgnoreInvisibleText?: boolean;
  SkipNumericSymbolText?: boolean;
  EnableCacheLookup?: boolean;
  EnableTranslationContext?: boolean;
  TranslationContextMaxExamples?: number;
  TranslationContextMaxCharacters?: number;
  EnableGlossary?: boolean;
  EnableAutoTermExtraction?: boolean;
  GlossaryMaxTerms?: number;
  GlossaryMaxCharacters?: number;
  ManualEditsOverrideAi?: boolean;
  ReapplyRememberedTranslations?: boolean;
  CacheRetentionDays?: number;
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
  Note?: string | null;
  Enabled?: boolean;
  UsageCount?: number;
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
