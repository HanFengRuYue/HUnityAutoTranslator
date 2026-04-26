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

export interface ProviderStatus {
  State: string;
  Message: string;
  CheckedUtc: string | null;
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
  Style: string;
  ProviderKind: string;
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
  CustomInstruction: string | null;
  CustomPrompt: string | null;
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
  FontSizeAdjustmentMode: string;
  FontSizeAdjustmentValue: number;
  LastError: string | null;
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
  ProviderKind?: string;
  BaseUrl?: string;
  Endpoint?: string;
  Model?: string;
  Style?: string;
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
  CustomInstruction?: string | null;
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
  FontSizeAdjustmentMode?: string;
  FontSizeAdjustmentValue?: number;
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
