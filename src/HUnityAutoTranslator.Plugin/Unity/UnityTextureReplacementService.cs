using System.Collections.Concurrent;
using System.Diagnostics;
using HUnityAutoTranslator.Core.Http;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx.Logging;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Textures;
using HUnityAutoTranslator.Plugin.Capture;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HUnityAutoTranslator.Plugin.Unity;

internal sealed class UnityTextureReplacementService : IDisposable
{
    private const string RawImageTypeName = "UnityEngine.UI.RawImage, UnityEngine.UI";
    private const string ImageTypeName = "UnityEngine.UI.Image, UnityEngine.UI";
    private const string ImageSpriteLabel = "Image.sprite";
    private const int MaxLoggedErrors = 40;
    private const int MaxTextureScanTargetsPerTick = 1;
    private const int MaxTextureScanSliceMilliseconds = 8;
    private const int MaxAutomaticTextureCapturePixels = 4096 * 4096;
    private const float TextureReferencePruneIntervalSeconds = 5f;

    private readonly TextureOverrideStore _store;
    private readonly TextureCatalogStore _catalogStore;
    private readonly TextureTextAnalysisStore _textAnalysisStore;
    private readonly Func<string?> _gameTitleProvider;
    private readonly ManualLogSource _logger;
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, RuntimeTextureRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UnityEngine.Object> _originalAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _replacementTextures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, OverrideApplicationKey> _appliedOverrides = new(StringComparer.Ordinal);
    private DateTimeOffset? _scannedUtc;
    private IReadOnlyList<string> _lastErrors = Array.Empty<string>();
    private int _lastDeferredTargetCount;
    private int _lastDeferredTextureCount;
    private TextureScanSession? _scanSession;
    private string _scanStatusMessage = "空闲";
    private bool _startupScanAttempted;
    private bool _hasPersistedOverrides;
    private string? _lastAppliedSceneName;
    private float _nextTextureReferencePruneTime;
    private bool _disposed;

    public UnityTextureReplacementService(
        TextureOverrideStore store,
        TextureCatalogStore catalogStore,
        TextureTextAnalysisStore textAnalysisStore,
        Func<string?> gameTitleProvider,
        ManualLogSource logger)
    {
        _store = store;
        _catalogStore = catalogStore;
        _textAnalysisStore = textAnalysisStore;
        _gameTitleProvider = gameTitleProvider;
        _logger = logger;
        _hasPersistedOverrides = _store.OverrideCount > 0;
    }

    public void Tick()
    {
        if (_startupScanAttempted &&
            _scanSession == null &&
            _mainThreadActions.IsEmpty &&
            !_hasPersistedOverrides &&
            HasNoTextureRuntimeWork())
        {
            return;
        }

        while (_mainThreadActions.TryDequeue(out var action))
        {
            action();
        }

        ProcessScanSlice();
        if (Time.unscaledTime >= _nextTextureReferencePruneTime)
        {
            PruneDeadTextureReferences();
            _nextTextureReferencePruneTime = Time.unscaledTime + TextureReferencePruneIntervalSeconds;
        }

        var currentSceneName = ActiveSceneName();
        if (!_startupScanAttempted)
        {
            _startupScanAttempted = true;
            _hasPersistedOverrides = _store.OverrideCount > 0;
            _lastAppliedSceneName = currentSceneName;
            if (_hasPersistedOverrides)
            {
                StartApplyOverridesOnMainThread("正在扫描当前场景并应用已导入贴图。");
            }
            return;
        }

        if (_hasPersistedOverrides &&
            !string.Equals(_lastAppliedSceneName, currentSceneName, StringComparison.Ordinal))
        {
            _lastAppliedSceneName = currentSceneName;
            StartApplyOverridesOnMainThread("场景变化，正在扫描并应用已导入贴图。");
        }
    }

    public Task<TextureScanResult> RequestScanAsync(TextureScanRequest? request = null)
    {
        var includeDeferredLargeTextures = request?.IncludeDeferredLargeTextures == true;
        var message = includeDeferredLargeTextures
            ? "正在扫描贴图（包含超大贴图）。"
            : "正在扫描贴图。";
        return EnqueueOnMainThread(() => StartScanOnMainThread(
            applyOverrides: true,
            message,
            includeDeferredLargeTextures: includeDeferredLargeTextures));
    }

    public Task<byte[]> ExportArchiveAsync(string? sceneName = null)
    {
        var items = _catalogStore.GetItemsForExport(sceneName, _store.LoadIndex());
        var archive = _store.ExportArchive(
            items,
            item => _catalogStore.TryReadSourceBytes(item.SourceHash, out var bytes) ? bytes : Array.Empty<byte>(),
            _gameTitleProvider());
        return Task.FromResult(archive);
    }

    public Task ExportArchiveAsync(Stream output, string? sceneName = null)
    {
        var items = _catalogStore.GetItemsForExport(sceneName, _store.LoadIndex());
        _store.ExportArchive(
            output,
            items,
            (item, stream) => _catalogStore.WriteSourceBytes(item.SourceHash, stream),
            _gameTitleProvider());
        return Task.CompletedTask;
    }

    public TextureCatalogPage GetCatalog(TextureCatalogQuery query)
    {
        var effectiveQuery = string.IsNullOrWhiteSpace(query.TextStatus)
            ? query
            : query with { Offset = 0, Limit = int.MaxValue, TextStatus = null };
        var result = _catalogStore.Query(effectiveQuery, _store.LoadIndex());
        var statusFilter = ParseTextStatus(query.TextStatus);
        var items = result.Items
            .Select(AttachTextAnalysis)
            .Where(item => statusFilter == null || item.TextAnalysis?.Status == statusFilter.Value)
            .ToArray();
        var pageItems = string.IsNullOrWhiteSpace(query.TextStatus)
            ? items
            : items.Skip(Math.Max(0, query.Offset)).Take(NormalizeTexturePageLimit(query.Limit)).ToArray();
        return new TextureCatalogPage(
            _scannedUtc,
            result.TotalCount,
            result.ReferenceCount,
            _store.OverrideCount,
            pageItems,
            _lastErrors,
            result.TotalCount,
            string.IsNullOrWhiteSpace(query.TextStatus) ? result.FilteredCount : items.Length,
            result.Offset,
            result.Limit,
            result.Scenes,
            GetScanStatus());
    }

    public bool TryGetSourceImage(string sourceHash, out byte[] bytes)
    {
        return _catalogStore.TryReadSourceBytes(sourceHash, out bytes);
    }

    public bool TryGetTextureImage(string sourceHash, string? variant, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.Equals(variant, "override", StringComparison.OrdinalIgnoreCase))
        {
            return _store.TryReadOverrideBytes(sourceHash, out bytes);
        }

        if (string.IsNullOrWhiteSpace(variant) || string.Equals(variant, "source", StringComparison.OrdinalIgnoreCase))
        {
            return TryGetSourceImage(sourceHash, out bytes);
        }

        return false;
    }

    public TextureRuntimeMemoryDiagnostics GetMemoryDiagnostics()
    {
        PruneDeadTextureReferences();
        lock (_gate)
        {
            return new TextureRuntimeMemoryDiagnostics(
                _records.Count,
                _records.Values.Sum(record => record.TargetCount),
                _originalAssets.Count,
                _replacementTextures.Count,
                _appliedOverrides.Count,
                RetainedSourcePngBytes: 0);
        }
    }

    private bool HasNoTextureRuntimeWork()
    {
        lock (_gate)
        {
            return _records.Count == 0 && _replacementTextures.Count == 0;
        }
    }

    public async Task<TextureTextDetectionResult> AnalyzeTextTexturesAsync(
        TextureTextDetectionRequest? request,
        IReadOnlyList<TextureImageProviderRuntimeProfile> profiles,
        IHttpTransport httpTransport,
        CancellationToken cancellationToken)
    {
        var items = ResolveRequestedItems(request?.SourceHashes);
        var errors = new List<string>();
        var updated = new List<TextureTextAnalysis>();
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_catalogStore.TryReadSourceBytes(item.SourceHash, out var sourceBytes))
            {
                errors.Add($"贴图源图不存在：{item.TextureName}");
                continue;
            }

            var analysis = TextureTextDetector.Analyze(item, sourceBytes, DateTimeOffset.UtcNow);
            var visionProfiles = profiles
                .Where(profile => ShouldUseVisionConfirmation(analysis, profile.Config, profile.ApiKey))
                .ToArray();
            if (visionProfiles.Length > 0)
            {
                var visionErrors = new List<string>();
                foreach (var visionProfile in visionProfiles)
                {
                    var vision = new TextureVisionTextClient(httpTransport, () => visionProfile.ApiKey);
                    try
                    {
                        analysis = ApplyVisionResult(
                            analysis,
                            await vision.DetectAsync(visionProfile.Config, item, sourceBytes, cancellationToken).ConfigureAwait(false));
                        visionErrors.Clear();
                        break;
                    }
                    catch (Exception ex) when (IsTextureImageProviderFailure(ex))
                    {
                        visionErrors.Add($"贴图文字视觉确认失败：{item.TextureName}：{visionProfile.Name}：{ex.Message}");
                    }
                }

                if (visionErrors.Count > 0)
                {
                    analysis = analysis with
                    {
                        Status = TextureTextStatus.NeedsManualReview,
                        NeedsManualReview = true,
                        LastError = visionErrors.Last()
                    };
                    errors.AddRange(visionErrors);
                }
            }

            updated.Add(_textAnalysisStore.Upsert(analysis));
        }

        return new TextureTextDetectionResult(items.Count, updated.Count, updated, errors);
    }

    public TextureTextStatusUpdateResult MarkTextStatus(TextureTextStatusUpdateRequest? request)
    {
        if (request?.SourceHashes == null || request.SourceHashes.Count == 0)
        {
            return new TextureTextStatusUpdateResult(0, Array.Empty<TextureTextAnalysis>(), new[] { "请先选择贴图。" });
        }

        if (!TryParseManualTextStatus(request.Status, out var status))
        {
            return new TextureTextStatusUpdateResult(0, Array.Empty<TextureTextAnalysis>(), new[] { "贴图文字状态无效。" });
        }

        var updated = new List<TextureTextAnalysis>();
        foreach (var hash in request.SourceHashes.Where(hash => !string.IsNullOrWhiteSpace(hash)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            updated.Add(_textAnalysisStore.Mark(hash, status, DateTimeOffset.UtcNow));
        }

        return new TextureTextStatusUpdateResult(updated.Count, updated, Array.Empty<string>());
    }

    public async Task<TextureImageTranslateResult> TranslateTextTexturesAsync(
        TextureImageTranslateRequest? request,
        RuntimeConfig config,
        IReadOnlyList<TextureImageProviderRuntimeProfile> profiles,
        IHttpTransport httpTransport,
        CancellationToken cancellationToken)
    {
        var readyProfiles = profiles
            .Where(profile => profile.Config.Enabled && !string.IsNullOrWhiteSpace(profile.ApiKey))
            .ToArray();
        if (readyProfiles.Length == 0 && !config.TextureImageTranslation.Enabled)
        {
            return new TextureImageTranslateResult(0, 0, 0, Array.Empty<TextureTextAnalysis>(), new[] { "请先在 AI 翻译设置中启用贴图文字翻译。" });
        }

        if (readyProfiles.Length == 0)
        {
            return new TextureImageTranslateResult(0, 0, 0, Array.Empty<TextureTextAnalysis>(), new[] { "请先保存可用的贴图图片服务配置和 API Key。" });
        }

        var items = ResolveRequestedItems(request?.SourceHashes);
        var generated = new List<TextureTextAnalysis>();
        var errors = new List<string>();
        var generatedCount = 0;
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentAnalysis = item.TextAnalysis ?? _textAnalysisStore.GetOrUnknown(item.SourceHash);
            if (request?.Force != true && !CanGenerate(currentAnalysis.Status))
            {
                errors.Add($"跳过未确认的贴图：{item.TextureName}");
                continue;
            }

            if (!_catalogStore.TryReadSourceBytes(item.SourceHash, out var sourceBytes))
            {
                errors.Add($"贴图源图不存在：{item.TextureName}");
                continue;
            }

            try
            {
                var preparation = TextureImagePreprocessor.PrepareForEdit(sourceBytes);
                var result = await GenerateTextureImageWithFailoverAsync(
                    item,
                    readyProfiles,
                    httpTransport,
                    BuildTextureImagePrompt(item, currentAnalysis, config.TargetLanguage),
                    preparation,
                    cancellationToken).ConfigureAwait(false);
                if (result.PngBytes == null)
                {
                    errors.AddRange(result.Errors);
                    generated.Add(_textAnalysisStore.Upsert(currentAnalysis with
                    {
                        Status = TextureTextStatus.Failed,
                        NeedsManualReview = true,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        LastError = result.LastError
                    }));
                    continue;
                }

                var saveResult = _store.SaveOverride(item, result.PngBytes);
                if (saveResult.ImportedCount <= 0)
                {
                    var message = saveResult.Errors.FirstOrDefault() ?? "生成贴图保存失败。";
                    errors.Add(message);
                    generated.Add(_textAnalysisStore.Upsert(currentAnalysis with
                    {
                        Status = TextureTextStatus.Failed,
                        NeedsManualReview = true,
                        UpdatedUtc = DateTimeOffset.UtcNow,
                        LastError = message
                    }));
                    continue;
                }

                generatedCount++;
                generated.Add(_textAnalysisStore.Upsert(currentAnalysis with
                {
                    Status = TextureTextStatus.Generated,
                    NeedsManualReview = false,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    LastError = null
                }));
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                errors.Add($"贴图源图预处理失败：{item.TextureName}：{ex.Message}");
                generated.Add(_textAnalysisStore.Upsert(currentAnalysis with
                {
                    Status = TextureTextStatus.Failed,
                    NeedsManualReview = true,
                    UpdatedUtc = DateTimeOffset.UtcNow,
                    LastError = ex.Message
                }));
            }
        }

        var applied = generatedCount > 0
            ? await EnqueueOnMainThread(() =>
            {
                ClearReplacementTextureCache();
                _hasPersistedOverrides = _store.OverrideCount > 0;
                _lastAppliedSceneName = ActiveSceneName();
                return ApplyOverridesToKnownTargets();
            }).ConfigureAwait(false)
            : 0;
        return new TextureImageTranslateResult(items.Count, generatedCount, applied, generated, errors);
    }

    private async Task<TextureImageGenerationResult> GenerateTextureImageWithFailoverAsync(
        TextureCatalogItem item,
        IReadOnlyList<TextureImageProviderRuntimeProfile> profiles,
        IHttpTransport httpTransport,
        string prompt,
        PreparedTextureImage preparation,
        CancellationToken cancellationToken)
    {
        var providerErrors = new List<string>();
        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var client = new TextureImageEditClient(httpTransport, () => profile.ApiKey);
            try
            {
                var result = await client.EditAsync(
                    profile.Config,
                    prompt,
                    preparation.PngBytes,
                    preparation.RequestSize,
                    cancellationToken).ConfigureAwait(false);
                return new TextureImageGenerationResult(
                    TextureImagePreprocessor.RestoreGeneratedImage(preparation, result.PngBytes),
                    null,
                    Array.Empty<string>());
            }
            catch (Exception ex) when (IsTextureImageProviderFailure(ex))
            {
                var message = $"贴图翻译生成失败：{item.TextureName}：{profile.Name}：{ex.Message}";
                providerErrors.Add(message);
                _logger.LogWarning(message);
            }
        }

        return new TextureImageGenerationResult(null, providerErrors.LastOrDefault(), providerErrors);
    }

    private static bool IsTextureImageProviderFailure(Exception ex)
    {
        // 传输层失败已统一映射成 InvalidOperationException（贴图客户端里抛出）；
        // 不再有 HttpRequestException/TaskCanceledException 直接冒泡。
        return ex is InvalidOperationException
            or Newtonsoft.Json.JsonException
            or FormatException
            or InvalidDataException
            or NotSupportedException;
    }

    private sealed record TextureImageGenerationResult(
        byte[]? PngBytes,
        string? LastError,
        IReadOnlyList<string> Errors);

    public async Task<TextureImportResult> ImportOverridesAsync(byte[] archiveBytes)
    {
        var currentItems = _catalogStore.GetItemsForExport(null, _store.LoadIndex());
        if (currentItems.Count == 0)
        {
            return new TextureImportResult(0, 0, new[] { "请先扫描贴图目录后再导入贴图包。" });
        }

        var importResult = _store.ImportArchive(archiveBytes, currentItems);
        var applied = importResult.ImportedCount > 0
            ? await EnqueueOnMainThread(() =>
            {
                ClearReplacementTextureCache();
                var appliedCount = ApplyOverridesToKnownTargets();
                _hasPersistedOverrides = _store.OverrideCount > 0;
                _lastAppliedSceneName = ActiveSceneName();
                return appliedCount;
            }).ConfigureAwait(false)
            : 0;
        return importResult with { AppliedCount = applied };
    }

    public async Task<TextureImportResult> ImportOverridesAsync(Stream archiveStream)
    {
        var currentItems = _catalogStore.GetItemsForExport(null, _store.LoadIndex());
        if (currentItems.Count == 0)
        {
            return new TextureImportResult(0, 0, new[] { "请先扫描贴图目录后再导入贴图包。" });
        }

        var importResult = _store.ImportArchive(archiveStream, currentItems);
        var applied = importResult.ImportedCount > 0
            ? await EnqueueOnMainThread(() =>
            {
                ClearReplacementTextureCache();
                var appliedCount = ApplyOverridesToKnownTargets();
                _hasPersistedOverrides = _store.OverrideCount > 0;
                _lastAppliedSceneName = ActiveSceneName();
                return appliedCount;
            }).ConfigureAwait(false)
            : 0;
        return importResult with { AppliedCount = applied };
    }

    public async Task<TextureOverrideClearResult> ClearOverridesAsync()
    {
        var clearResult = _store.ClearOverrides();
        var restored = await EnqueueOnMainThread(() =>
        {
            var restoredCount = RestoreKnownTargets();
            ClearReplacementTextureCache();
            _hasPersistedOverrides = _store.OverrideCount > 0;
            _lastAppliedSceneName = ActiveSceneName();
            return restoredCount;
        }).ConfigureAwait(false);
        return clearResult with { RestoredCount = restored };
    }

    public void Dispose()
    {
        _disposed = true;
        ClearReplacementTextureCache();
    }

    private TextureCatalogItem AttachTextAnalysis(TextureCatalogItem item)
    {
        return _textAnalysisStore.TryGet(item.SourceHash, out var analysis) && analysis != null
            ? item with { TextAnalysis = analysis }
            : item with { TextAnalysis = TextureTextAnalysis.Unknown(item.SourceHash) };
    }

    private IReadOnlyList<TextureCatalogItem> ResolveRequestedItems(IReadOnlyList<string>? sourceHashes)
    {
        var all = _catalogStore
            .Query(new TextureCatalogQuery(null, 0, int.MaxValue), _store.LoadIndex())
            .Items
            .Select(AttachTextAnalysis)
            .ToArray();
        if (sourceHashes == null || sourceHashes.Count == 0)
        {
            return all;
        }

        var selected = new HashSet<string>(sourceHashes.Where(hash => !string.IsNullOrWhiteSpace(hash)), StringComparer.OrdinalIgnoreCase);
        return all.Where(item => selected.Contains(item.SourceHash)).ToArray();
    }

    private static TextureTextStatus? ParseTextStatus(string? value)
    {
        return Enum.TryParse<TextureTextStatus>(value, ignoreCase: true, out var status)
            ? status
            : null;
    }

    private static bool TryParseManualTextStatus(string? value, out TextureTextStatus status)
    {
        if (Enum.TryParse(value, ignoreCase: true, out status))
        {
            return status is TextureTextStatus.ConfirmedText or
                TextureTextStatus.NoText or
                TextureTextStatus.NeedsManualReview or
                TextureTextStatus.Candidate;
        }

        status = TextureTextStatus.Unknown;
        return false;
    }

    private static int NormalizeTexturePageLimit(int limit)
    {
        if (limit <= 0)
        {
            return 20;
        }

        return Math.Min(500, limit);
    }

    private static bool ShouldUseVisionConfirmation(TextureTextAnalysis analysis, TextureImageTranslationConfig config, string? apiKey)
    {
        return config.Enabled &&
            config.EnableVisionConfirmation &&
            !string.IsNullOrWhiteSpace(apiKey) &&
            analysis.Status is TextureTextStatus.Candidate or TextureTextStatus.NeedsManualReview;
    }

    private static TextureTextAnalysis ApplyVisionResult(TextureTextAnalysis local, TextureVisionTextResult vision)
    {
        var status = vision.HasText
            ? vision.Confidence >= 0.7 ? TextureTextStatus.ConfirmedText : TextureTextStatus.NeedsManualReview
            : vision.Confidence >= 0.8 ? TextureTextStatus.LikelyNoText : TextureTextStatus.NeedsManualReview;
        return local with
        {
            Status = status,
            Confidence = Math.Max(local.Confidence, vision.Confidence),
            DetectedText = string.IsNullOrWhiteSpace(vision.DetectedText) ? local.DetectedText : vision.DetectedText,
            Reason = string.IsNullOrWhiteSpace(vision.Reason) ? local.Reason : vision.Reason,
            NeedsManualReview = vision.NeedsManualReview || status == TextureTextStatus.NeedsManualReview,
            UpdatedUtc = DateTimeOffset.UtcNow,
            LastError = null
        };
    }

    private static bool CanGenerate(TextureTextStatus status)
    {
        return status is TextureTextStatus.ConfirmedText or TextureTextStatus.NeedsManualReview or TextureTextStatus.Candidate or TextureTextStatus.Generated;
    }

    private string BuildTextureImagePrompt(TextureCatalogItem item, TextureTextAnalysis analysis, string targetLanguage)
    {
        var detectedText = string.IsNullOrWhiteSpace(analysis.DetectedText)
            ? "unknown or stylized source text"
            : analysis.DetectedText;
        var gameTitle = _gameTitleProvider();
        return "Translate every visible text element in this game texture to " + targetLanguage + ". " +
            "Keep the same image dimensions, composition, icons, background, colors, borders, lighting, texture style, and poster/art lettering placement. " +
            "Do not add new objects, watermarks, captions, logos, or explanatory text. " +
            "Only replace the original visible text with a natural localized translation. " +
            $"Detected source text: {detectedText}. Texture name: {item.TextureName}. Game: {gameTitle ?? "unknown"}.";
    }

    private Task<T> EnqueueOnMainThread<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_disposed)
        {
            completion.SetException(new ObjectDisposedException(nameof(UnityTextureReplacementService)));
            return completion.Task;
        }

        _mainThreadActions.Enqueue(() =>
        {
            try
            {
                completion.SetResult(action());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });
        return completion.Task;
    }

    private void ClearReplacementTextureCache()
    {
        foreach (var texture in _replacementTextures.Values)
        {
            if (texture != null)
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        _replacementTextures.Clear();
    }

    private TextureScanResult StartApplyOverridesOnMainThread(string message)
    {
        return StartScanOnMainThread(applyOverrides: true, message, applyOnly: true);
    }

    private TextureScanResult StartScanOnMainThread(
        bool applyOverrides,
        string message,
        bool applyOnly = false,
        bool includeDeferredLargeTextures = false)
    {
        if (_scanSession != null)
        {
            return BuildScanResult("贴图扫描已在进行中。");
        }

        var errors = new List<string>();
        var overrideIndex = applyOverrides ? _store.LoadIndex() : TextureOverrideIndex.Empty;
        var overrideDimensions = new HashSet<string>(
            overrideIndex.Records.Select(record => DimensionKey(record.Width, record.Height)),
            StringComparer.Ordinal);
        if (applyOnly && overrideIndex.Records.Count == 0)
        {
            return BuildScanResult("没有需要应用的覆盖贴图。");
        }

        _scanSession = new TextureScanSession(
            EnumerateTargets(errors).GetEnumerator(),
            applyOverrides,
            applyOnly,
            includeDeferredLargeTextures,
            overrideDimensions,
            DateTimeOffset.UtcNow,
            errors);
        _scanStatusMessage = message;
        _lastErrors = Array.Empty<string>();
        _lastDeferredTargetCount = 0;
        _lastDeferredTextureCount = 0;
        return BuildScanResult("贴图扫描已开始。");
    }

    private void ProcessScanSlice()
    {
        var session = _scanSession;
        if (session == null)
        {
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var processedThisTick = 0;
        while (processedThisTick < MaxTextureScanTargetsPerTick &&
            (processedThisTick == 0 || stopwatch.ElapsedMilliseconds < MaxTextureScanSliceMilliseconds))
        {
            bool hasTarget;
            try
            {
                hasTarget = session.Targets.MoveNext();
            }
            catch (Exception ex)
            {
                AddError(session.Errors, $"贴图枚举失败：{ex.Message}");
                CompleteScanSession(session);
                return;
            }

            if (!hasTarget)
            {
                CompleteScanSession(session);
                return;
            }

            ProcessScanTarget(session, session.Targets.Current);
            processedThisTick++;
        }
    }

    private void ProcessScanTarget(TextureScanSession session, ITextureTarget target)
    {
        session.ProcessedTargets++;
        if (session.ApplyOnly && !session.OverrideDimensions.Contains(DimensionKey(target.Width, target.Height)))
        {
            return;
        }

        if (!session.IncludeDeferredLargeTextures && ShouldDeferAutomaticCapture(target))
        {
            session.Defer(target);
            AddError(session.Errors, BuildDeferredLargeTextureMessage(target));
            return;
        }

        if (!target.TryCapturePng(out var pngBytes, out var error))
        {
            AddError(session.Errors, $"跳过贴图 {target.TextureName}：{error}");
            return;
        }

        var hash = ComputeHash(pngBytes);
        TextureOverrideRecord? overrideRecord = null;
        var hasOverride = session.ApplyOverrides &&
            _store.TryGetOverride(hash, out overrideRecord) &&
            overrideRecord != null;
        if (session.ApplyOnly && !hasOverride)
        {
            return;
        }

        var fileName = TextureArchiveNaming.BuildTextureEntryName(hash, target.TextureName);
        RuntimeTextureRecord record;
        lock (_gate)
        {
            if (!_records.TryGetValue(hash, out record!))
            {
                record = new RuntimeTextureRecord(
                    hash,
                    target.TextureName,
                    target.Width,
                    target.Height,
                    target.Format,
                    fileName);
                _records.Add(hash, record);
            }

            record.AddTarget(target);
        }

        if (!session.ApplyOnly)
        {
            _catalogStore.Upsert(
                new TextureCatalogItem(
                    hash,
                    target.TextureName,
                    target.Width,
                    target.Height,
                    target.Format,
                    fileName,
                    1,
                    new[]
                    {
                        new TextureReferenceInfo(
                            target.TargetId,
                            target.SceneName,
                            target.HierarchyPath,
                            target.ComponentType)
                    },
                    false,
                    null),
                pngBytes);
            session.DiscoveredHashes.Add(hash);
            session.DiscoveredReferenceCount++;
        }
        else if (hasOverride)
        {
            session.DiscoveredHashes.Add(hash);
            session.DiscoveredReferenceCount++;
        }

        var applyError = string.Empty;
        if (session.ApplyOverrides && hasOverride && TryApplyOverrideToTarget(record, target, overrideRecord!, out applyError))
        {
            session.AppliedOverrideCount++;
        }
        else if (!string.IsNullOrWhiteSpace(applyError))
        {
            AddError(session.Errors, applyError);
        }
    }

    private void CompleteScanSession(TextureScanSession session)
    {
        session.Targets.Dispose();
        PruneDeadTextureReferences();
        if (!session.ApplyOnly)
        {
            _catalogStore.Save();
        }

        _scannedUtc = DateTimeOffset.UtcNow;
        _lastDeferredTargetCount = session.DeferredTargetCount;
        _lastDeferredTextureCount = session.DeferredTextureCount;
        _lastErrors = session.Errors.ToArray();
        _scanStatusMessage = "空闲";
        _scanSession = null;
        if (session.DiscoveredHashes.Count > 0 || session.Errors.Count > 0)
        {
            _logger.LogInfo($"贴图扫描完成：{session.DiscoveredHashes.Count} 张贴图，{session.DiscoveredReferenceCount} 个引用，已应用 {session.AppliedOverrideCount} 个覆盖。");
        }
    }

    private TextureCatalogScanStatus GetScanStatus()
    {
        var session = _scanSession;
        if (session == null)
        {
            return TextureCatalogScanStatus.Idle(_scannedUtc, _lastDeferredTargetCount, _lastDeferredTextureCount);
        }

        return new TextureCatalogScanStatus(
            true,
            _scanStatusMessage,
            session.StartedUtc,
            null,
            session.ProcessedTargets,
            session.DiscoveredHashes.Count,
            session.DiscoveredReferenceCount,
            session.DeferredTargetCount,
            session.DeferredTextureCount);
    }

    private TextureScanResult BuildScanResult(string message)
    {
        var catalog = _catalogStore.Query(new TextureCatalogQuery(null, 0, 1), _store.LoadIndex());
        var status = GetScanStatus();
        return new TextureScanResult(
            _scannedUtc ?? DateTimeOffset.UtcNow,
            catalog.TotalCount,
            catalog.ReferenceCount,
            _store.OverrideCount,
            _lastErrors,
            status.DeferredTargetCount,
            status.DeferredTextureCount,
            status.IsScanning,
            message);
    }

    private int ApplyOverridesToKnownTargets()
    {
        var applied = 0;
        foreach (var record in _records.Values.ToArray())
        {
            if (!_store.TryGetOverride(record.SourceHash, out var overrideRecord) || overrideRecord == null)
            {
                continue;
            }

            var pendingTargets = record.Targets
                .Where(target => target.IsAlive)
                .Where(target => !SkipAlreadyAppliedOverride(target, overrideRecord))
                .ToArray();
            if (pendingTargets.Length == 0)
            {
                continue;
            }

            if (!_store.TryReadOverrideBytes(record.SourceHash, out var bytes))
            {
                continue;
            }

            if (!TryGetReplacementTexture(record, overrideRecord, bytes, out var replacement, out var error))
            {
                _logger.LogWarning($"贴图覆盖加载失败：{record.TextureName}，{error}");
                continue;
            }

            foreach (var target in pendingTargets)
            {
                if (target.ApplyOverride(replacement, out var applyError))
                {
                    _appliedOverrides[target.TargetId] = BuildOverrideApplicationKey(overrideRecord);
                    applied++;
                }
                else
                {
                    _logger.LogWarning($"贴图覆盖应用失败：{target.ComponentType}，{applyError}");
                }
            }
        }

        return applied;
    }

    private bool TryApplyOverrideToTarget(RuntimeTextureRecord record, ITextureTarget target, TextureOverrideRecord overrideRecord, out string error)
    {
        error = string.Empty;
        if (SkipAlreadyAppliedOverride(target, overrideRecord))
        {
            return false;
        }

        if (!_store.TryReadOverrideBytes(record.SourceHash, out var bytes))
        {
            return false;
        }

        if (!TryGetReplacementTexture(record, overrideRecord, bytes, out var replacement, out var loadError))
        {
            error = $"贴图覆盖加载失败：{record.TextureName}，{loadError}";
            return false;
        }

        if (target.ApplyOverride(replacement, out var applyError))
        {
            _appliedOverrides[target.TargetId] = BuildOverrideApplicationKey(overrideRecord);
            return true;
        }

        error = $"贴图覆盖应用失败：{target.ComponentType}，{applyError}";
        return false;
    }

    private bool SkipAlreadyAppliedOverride(ITextureTarget target, TextureOverrideRecord overrideRecord)
    {
        return _appliedOverrides.TryGetValue(target.TargetId, out var applied) &&
            applied == BuildOverrideApplicationKey(overrideRecord);
    }

    private static OverrideApplicationKey BuildOverrideApplicationKey(TextureOverrideRecord overrideRecord)
    {
        return new OverrideApplicationKey(overrideRecord.SourceHash, overrideRecord.UpdatedUtc);
    }

    private int RestoreKnownTargets()
    {
        var restored = 0;
        foreach (var target in _records.Values.SelectMany(record => record.Targets).ToArray())
        {
            if (!target.IsAlive)
            {
                _appliedOverrides.Remove(target.TargetId);
                continue;
            }

            if (target.RestoreOriginal(out var error))
            {
                _appliedOverrides.Remove(target.TargetId);
                restored++;
            }
            else if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning($"恢复原始贴图失败：{target.ComponentType}，{error}");
            }
        }

        return restored;
    }

    private bool TryGetReplacementTexture(
        RuntimeTextureRecord record,
        TextureOverrideRecord overrideRecord,
        byte[] bytes,
        out Texture2D replacement,
        out string error)
    {
        replacement = null!;
        error = string.Empty;
        var cacheKey = record.SourceHash + ":" + overrideRecord.UpdatedUtc.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (_replacementTextures.TryGetValue(cacheKey, out var cached) && cached != null)
        {
            replacement = cached;
            return true;
        }

        var texture = new Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: true))
        {
            UnityEngine.Object.Destroy(texture);
            error = "PNG 解码失败。";
            return false;
        }

        if (texture.width != record.Width || texture.height != record.Height)
        {
            UnityEngine.Object.Destroy(texture);
            error = $"尺寸不匹配：{texture.width}x{texture.height}，原始 {record.Width}x{record.Height}。";
            return false;
        }

        texture.name = "HUnityTextureOverride_" + record.SourceHash;
        _replacementTextures[cacheKey] = texture;
        replacement = texture;
        return true;
    }

    private IEnumerable<ITextureTarget> EnumerateTargets(List<string> errors)
    {
        foreach (var target in EnumerateRawImageTargets(errors))
        {
            yield return target;
        }

        foreach (var target in EnumerateUguiImageTargets(errors))
        {
            yield return target;
        }

        foreach (var target in EnumerateSpriteRendererTargets(errors))
        {
            yield return target;
        }

        foreach (var target in EnumerateRendererTargets(errors))
        {
            yield return target;
        }
    }

    private IEnumerable<ITextureTarget> EnumerateRawImageTargets(List<string> errors)
    {
        var rawImageType = Type.GetType(RawImageTypeName);
        var textureProperty = rawImageType?.GetProperty("texture", BindingFlags.Instance | BindingFlags.Public);
        if (rawImageType == null || textureProperty == null)
        {
            yield break;
        }

        foreach (var obj in UnityObjectFinder.FindObjects(rawImageType))
        {
            if (obj is not Component component)
            {
                continue;
            }

            Texture? texture;
            try
            {
                texture = textureProperty.GetValue(component, null) as Texture;
            }
            catch (Exception ex)
            {
                AddError(errors, $"RawImage 贴图读取失败：{ex.Message}");
                continue;
            }

            if (texture == null || !HasValidSize(texture))
            {
                continue;
            }

            var targetId = TargetId("raw-image", component);
            var original = RememberOriginalAsset(targetId, texture);
            if (original is Texture originalTexture && HasValidSize(originalTexture))
            {
                yield return new RawImageTextureTarget(targetId, component, textureProperty, originalTexture);
            }
        }
    }

    private IEnumerable<ITextureTarget> EnumerateUguiImageTargets(List<string> errors)
    {
        var imageType = Type.GetType(ImageTypeName);
        var spriteProperty = imageType?.GetProperty("sprite", BindingFlags.Instance | BindingFlags.Public);
        if (imageType == null || spriteProperty == null)
        {
            yield break;
        }

        foreach (var obj in UnityObjectFinder.FindObjects(imageType))
        {
            if (obj is not Component component)
            {
                continue;
            }

            Sprite? sprite;
            try
            {
                sprite = spriteProperty.GetValue(component, null) as Sprite;
            }
            catch (Exception ex)
            {
                AddError(errors, $"{ImageSpriteLabel} 读取失败：{ex.Message}");
                continue;
            }

            if (sprite == null)
            {
                continue;
            }

            var targetId = TargetId("image", component);
            var original = RememberOriginalAsset(targetId, sprite);
            if (original is not Sprite originalSprite ||
                originalSprite.texture == null ||
                !HasValidSize(originalSprite.texture))
            {
                continue;
            }

            if (!TryGetSpriteTextureRect(originalSprite, out var textureRect, out var rectError))
            {
                AddError(errors, $"{rectError}：{BuildHierarchyPath(component.transform)}。");
                continue;
            }

            if (IsFullTextureSprite(originalSprite, textureRect))
            {
                yield return new ImageSpriteTarget(targetId, component, spriteProperty, originalSprite);
                continue;
            }

            yield return new SpriteSubregionTextureTarget(targetId, component, spriteProperty, null, originalSprite, textureRect);
        }
    }

    private IEnumerable<ITextureTarget> EnumerateSpriteRendererTargets(List<string> errors)
    {
        foreach (var renderer in UnityObjectFinder.FindObjects(typeof(SpriteRenderer)).OfType<SpriteRenderer>())
        {
            var sprite = renderer.sprite;
            if (sprite == null)
            {
                continue;
            }

            var targetId = TargetId("sprite-renderer", renderer);
            var original = RememberOriginalAsset(targetId, sprite);
            if (original is not Sprite originalSprite ||
                originalSprite.texture == null ||
                !HasValidSize(originalSprite.texture))
            {
                continue;
            }

            if (!TryGetSpriteTextureRect(originalSprite, out var textureRect, out var rectError))
            {
                AddError(errors, $"{rectError}：{BuildHierarchyPath(renderer.transform)}。");
                continue;
            }

            if (IsFullTextureSprite(originalSprite, textureRect))
            {
                yield return new SpriteRendererTextureTarget(targetId, renderer, originalSprite);
                continue;
            }

            yield return new SpriteSubregionTextureTarget(targetId, renderer, null, renderer, originalSprite, textureRect);
        }
    }

    private IEnumerable<ITextureTarget> EnumerateRendererTargets(List<string> errors)
    {
        foreach (var renderer in UnityObjectFinder.FindObjects(typeof(Renderer)).OfType<Renderer>())
        {
            if (renderer is SpriteRenderer)
            {
                continue;
            }

            Texture? texture = null;
            try
            {
                texture = renderer.sharedMaterial == null ? null : renderer.sharedMaterial.mainTexture;
            }
            catch (Exception ex)
            {
                AddError(errors, $"Renderer 贴图读取失败：{ex.Message}");
            }

            if (texture == null || !HasValidSize(texture))
            {
                continue;
            }

            var targetId = TargetId("renderer", renderer);
            var original = RememberOriginalAsset(targetId, texture);
            if (original is Texture originalTexture && HasValidSize(originalTexture))
            {
                yield return new RendererTextureTarget(targetId, renderer, originalTexture);
            }
        }
    }

    private UnityEngine.Object RememberOriginalAsset(string targetId, UnityEngine.Object current)
    {
        if (_originalAssets.TryGetValue(targetId, out var existing) && existing != null)
        {
            return existing;
        }

        _originalAssets[targetId] = current;
        return current;
    }

    private void PruneDeadTextureReferences()
    {
        lock (_gate)
        {
            foreach (var item in _records.ToArray())
            {
                item.Value.PruneDeadTargets();
                if (item.Value.TargetCount == 0 && !_store.TryGetOverride(item.Key, out _))
                {
                    _records.Remove(item.Key);
                }
            }

            var liveTargetIds = new HashSet<string>(
                _records.Values.SelectMany(record => record.TargetIds),
                StringComparer.Ordinal);
            foreach (var item in _originalAssets.ToArray())
            {
                if (item.Value == null || !liveTargetIds.Contains(item.Key))
                {
                    _originalAssets.Remove(item.Key);
                }
            }

            foreach (var targetId in _appliedOverrides.Keys.ToArray())
            {
                if (!liveTargetIds.Contains(targetId))
                {
                    _appliedOverrides.Remove(targetId);
                }
            }
        }
    }

    private static bool TryEncodeTexture(Texture texture, out byte[] pngBytes, out string error)
    {
        pngBytes = Array.Empty<byte>();
        error = string.Empty;
        if (texture is Texture2D texture2D)
        {
            try
            {
                pngBytes = ImageConversion.EncodeToPNG(texture2D);
                if (pngBytes.Length > 0)
                {
                    return true;
                }
            }
            catch
            {
                // Non-readable textures are copied through a RenderTexture below.
            }
        }

        return TryEncodeTextureViaReadPixels(texture, out pngBytes, out error);
    }

    private static bool TryEncodeTextureViaReadPixels(Texture texture, out byte[] pngBytes, out string error)
    {
        pngBytes = Array.Empty<byte>();
        error = string.Empty;
        RenderTexture? renderTexture = null;
        Texture2D? readable = null;
        var previous = RenderTexture.active;
        try
        {
            renderTexture = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(texture, renderTexture);
            RenderTexture.active = renderTexture;
            readable = new Texture2D(texture.width, texture.height, UnityEngine.TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0, false);
            readable.Apply(updateMipmaps: false);
            pngBytes = ImageConversion.EncodeToPNG(readable);
            return pngBytes.Length > 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            if (readable != null)
            {
                UnityEngine.Object.Destroy(readable);
            }
        }
    }

    private static string ComputeHash(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(bytes).Select(value => value.ToString("x2")));
    }

    private static bool HasValidSize(Texture texture)
    {
        return texture.width > 0 && texture.height > 0;
    }

    private static bool ShouldDeferAutomaticCapture(ITextureTarget target)
    {
        return (long)target.Width * target.Height > MaxAutomaticTextureCapturePixels;
    }

    private static string BuildDeferredLargeTextureMessage(ITextureTarget target)
    {
        return $"延迟扫描超大贴图：{target.TextureName}（{target.Width}x{target.Height}，{target.ComponentType}，{target.HierarchyPath}）。默认扫描会跳过超过 4096x4096 像素的贴图；需要处理时请单独点击“扫描超大贴图”。";
    }

    private static string DeferredTextureKey(ITextureTarget target)
    {
        return target.TextureName + "|" +
            target.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x" +
            target.Height.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" +
            target.Format;
    }

    private static string DimensionKey(int width, int height)
    {
        return width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "x" +
            height.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static bool TryGetSpriteTextureRect(Sprite sprite, out Rect textureRect, out string error)
    {
        textureRect = default;
        error = string.Empty;
        try
        {
            textureRect = sprite.textureRect;
            if (textureRect.width <= 0 || textureRect.height <= 0)
            {
                error = "Sprite 子区域尺寸无效";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"紧密打包图集暂不支持直接裁剪，已跳过该 Sprite：{ex.Message}";
            return false;
        }
    }

    private static bool IsFullTextureSprite(Sprite sprite, Rect textureRect)
    {
        return Math.Abs(textureRect.x) < 0.01f &&
            Math.Abs(textureRect.y) < 0.01f &&
            Math.Abs(textureRect.width - sprite.texture.width) < 0.01f &&
            Math.Abs(textureRect.height - sprite.texture.height) < 0.01f;
    }

    private static bool CropSpriteTextureRegion(
        Texture texture,
        Rect textureRect,
        out byte[] pngBytes,
        out string error)
    {
        pngBytes = Array.Empty<byte>();
        error = string.Empty;
        RenderTexture? renderTexture = null;
        Texture2D? readable = null;
        var previous = RenderTexture.active;
        try
        {
            var width = Math.Max(1, (int)Math.Round(textureRect.width));
            var height = Math.Max(1, (int)Math.Round(textureRect.height));
            renderTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var scale = new Vector2(textureRect.width / texture.width, textureRect.height / texture.height);
            var offset = new Vector2(textureRect.x / texture.width, textureRect.y / texture.height);
            Graphics.Blit(texture, renderTexture, scale, offset);
            RenderTexture.active = renderTexture;
            readable = new Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
            readable.Apply(updateMipmaps: false);
            pngBytes = ImageConversion.EncodeToPNG(readable);
            return pngBytes.Length > 0;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (renderTexture != null)
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }

            if (readable != null)
            {
                UnityEngine.Object.Destroy(readable);
            }
        }
    }

    private static string TargetId(string kind, Component component)
    {
        return kind + ":" + component.GetInstanceID();
    }

    private static string BuildHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        for (var current = transform; current != null; current = current.parent)
        {
            names.Push(current.name);
        }

        return string.Join("/", names);
    }

    private static string SceneName(Component component)
    {
        try
        {
            return component.gameObject.scene.name;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ActiveSceneName()
    {
        try
        {
            return SceneManager.GetActiveScene().name ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string TextureName(Texture texture)
    {
        return string.IsNullOrWhiteSpace(texture.name) ? "texture" : texture.name;
    }

    private static string TextureFormat(Texture texture)
    {
        return texture is Texture2D texture2D ? texture2D.format.ToString() : texture.GetType().Name;
    }

    private static void AddError(List<string> errors, string message)
    {
        if (errors.Count < MaxLoggedErrors)
        {
            errors.Add(message);
        }
    }

    private sealed record OverrideApplicationKey(string SourceHash, DateTimeOffset OverrideUpdatedUtc);

    private sealed class TextureScanSession
    {
        public TextureScanSession(
            IEnumerator<ITextureTarget> targets,
            bool applyOverrides,
            bool applyOnly,
            bool includeDeferredLargeTextures,
            HashSet<string> overrideDimensions,
            DateTimeOffset startedUtc,
            List<string> errors)
        {
            Targets = targets;
            ApplyOverrides = applyOverrides;
            ApplyOnly = applyOnly;
            IncludeDeferredLargeTextures = includeDeferredLargeTextures;
            OverrideDimensions = overrideDimensions;
            StartedUtc = startedUtc;
            Errors = errors;
        }

        public IEnumerator<ITextureTarget> Targets { get; }

        public bool ApplyOverrides { get; }

        public bool ApplyOnly { get; }

        public bool IncludeDeferredLargeTextures { get; }

        public HashSet<string> OverrideDimensions { get; }

        public DateTimeOffset StartedUtc { get; }

        public List<string> Errors { get; }

        public HashSet<string> DiscoveredHashes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int ProcessedTargets { get; set; }

        public int DiscoveredReferenceCount { get; set; }

        public int AppliedOverrideCount { get; set; }

        public int DeferredTargetCount { get; private set; }

        public int DeferredTextureCount => DeferredTextureKeys.Count;

        private HashSet<string> DeferredTextureKeys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public void Defer(ITextureTarget target)
        {
            DeferredTargetCount++;
            DeferredTextureKeys.Add(DeferredTextureKey(target));
        }
    }

    private sealed class RuntimeTextureRecord
    {
        private readonly Dictionary<string, ITextureTarget> _targets = new(StringComparer.Ordinal);

        public RuntimeTextureRecord(
            string sourceHash,
            string textureName,
            int width,
            int height,
            string format,
            string fileName)
        {
            SourceHash = sourceHash;
            TextureName = textureName;
            Width = width;
            Height = height;
            Format = format;
            FileName = fileName;
        }

        public string SourceHash { get; }

        public string TextureName { get; }

        public int Width { get; }

        public int Height { get; }

        public string Format { get; }

        public string FileName { get; }

        public IReadOnlyList<ITextureTarget> Targets => _targets.Values.ToArray();

        public IReadOnlyList<string> TargetIds => _targets.Keys.ToArray();

        public int TargetCount => _targets.Count;

        public void AddTarget(ITextureTarget target)
        {
            _targets[target.TargetId] = target;
        }

        public void PruneDeadTargets()
        {
            foreach (var item in _targets.ToArray())
            {
                if (!item.Value.IsAlive)
                {
                    _targets.Remove(item.Key);
                }
            }
        }

        public TextureCatalogItem ToCatalogItem(TextureOverrideRecord? overrideRecord)
        {
            return new TextureCatalogItem(
                SourceHash,
                TextureName,
                Width,
                Height,
                Format,
                FileName,
                _targets.Count,
                _targets.Values.Select(target => new TextureReferenceInfo(
                    target.TargetId,
                    target.SceneName,
                    target.HierarchyPath,
                    target.ComponentType)).ToArray(),
                overrideRecord != null,
                overrideRecord?.UpdatedUtc);
        }
    }

    public sealed record TextureRuntimeMemoryDiagnostics(
        int TextureRecordCount,
        int TextureTargetReferenceCount,
        int OriginalAssetReferenceCount,
        int ReplacementTextureCount,
        int AppliedOverrideCount,
        long RetainedSourcePngBytes);

    private interface ITextureTarget
    {
        string TargetId { get; }

        string TextureName { get; }

        int Width { get; }

        int Height { get; }

        string Format { get; }

        Texture SourceTexture { get; }

        string SceneName { get; }

        string HierarchyPath { get; }

        string ComponentType { get; }

        bool IsAlive { get; }

        bool TryCapturePng(out byte[] pngBytes, out string error);

        bool ApplyOverride(Texture2D replacement, out string error);

        bool RestoreOriginal(out string error);
    }

    private abstract class TextureTargetBase : ITextureTarget
    {
        protected TextureTargetBase(string targetId, Component component, Texture sourceTexture)
        {
            TargetId = targetId;
            Component = component;
            SourceTexture = sourceTexture;
        }

        public string TargetId { get; }

        public Component Component { get; }

        public Texture SourceTexture { get; }

        public virtual string TextureName => UnityTextureReplacementService.TextureName(SourceTexture);

        public virtual int Width => SourceTexture.width;

        public virtual int Height => SourceTexture.height;

        public virtual string Format => UnityTextureReplacementService.TextureFormat(SourceTexture);

        public string SceneName => UnityTextureReplacementService.SceneName(Component);

        public string HierarchyPath => BuildHierarchyPath(Component.transform);

        public string ComponentType => Component.GetType().FullName ?? Component.GetType().Name;

        public bool IsAlive => Component != null;

        public virtual bool TryCapturePng(out byte[] pngBytes, out string error)
        {
            return TryEncodeTexture(SourceTexture, out pngBytes, out error);
        }

        public abstract bool ApplyOverride(Texture2D replacement, out string error);

        public abstract bool RestoreOriginal(out string error);

        protected static void MarkDirty(Component component)
        {
            InvokeIfAvailable(component, "SetAllDirty");
            InvokeIfAvailable(component, "SetMaterialDirty");
            InvokeIfAvailable(component, "SetVerticesDirty");
        }

        protected static void InvokeIfAvailable(object target, string methodName)
        {
            try
            {
                target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null)
                    ?.Invoke(target, null);
            }
            catch
            {
            }
        }
    }

    private sealed class RawImageTextureTarget : TextureTargetBase
    {
        private readonly PropertyInfo _textureProperty;

        public RawImageTextureTarget(string targetId, Component component, PropertyInfo textureProperty, Texture originalTexture)
            : base(targetId, component, originalTexture)
        {
            _textureProperty = textureProperty;
        }

        public override bool ApplyOverride(Texture2D replacement, out string error)
        {
            return SetTexture(replacement, out error);
        }

        public override bool RestoreOriginal(out string error)
        {
            return SetTexture(SourceTexture, out error);
        }

        private bool SetTexture(Texture texture, out string error)
        {
            error = string.Empty;
            try
            {
                _textureProperty.SetValue(Component, texture, null);
                MarkDirty(Component);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private sealed class ImageSpriteTarget : TextureTargetBase
    {
        private readonly PropertyInfo _spriteProperty;
        private readonly Sprite _originalSprite;
        private Sprite? _replacementSprite;

        public ImageSpriteTarget(string targetId, Component component, PropertyInfo spriteProperty, Sprite originalSprite)
            : base(targetId, component, originalSprite.texture)
        {
            _spriteProperty = spriteProperty;
            _originalSprite = originalSprite;
        }

        public override bool ApplyOverride(Texture2D replacement, out string error)
        {
            var sprite = CreateSpriteFromOriginal(replacement, _originalSprite);
            if (SetSprite(sprite, out error))
            {
                DestroyReplacementSprite();
                _replacementSprite = sprite;
                return true;
            }

            UnityEngine.Object.Destroy(sprite);
            return false;
        }

        public override bool RestoreOriginal(out string error)
        {
            if (!SetSprite(_originalSprite, out error))
            {
                return false;
            }

            DestroyReplacementSprite();
            return true;
        }

        private bool SetSprite(Sprite sprite, out string error)
        {
            error = string.Empty;
            try
            {
                _spriteProperty.SetValue(Component, sprite, null);
                MarkDirty(Component);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void DestroyReplacementSprite()
        {
            if (_replacementSprite != null)
            {
                UnityEngine.Object.Destroy(_replacementSprite);
                _replacementSprite = null;
            }
        }
    }

    private sealed class SpriteRendererTextureTarget : TextureTargetBase
    {
        private readonly SpriteRenderer _renderer;
        private readonly Sprite _originalSprite;
        private Sprite? _replacementSprite;

        public SpriteRendererTextureTarget(string targetId, SpriteRenderer renderer, Sprite originalSprite)
            : base(targetId, renderer, originalSprite.texture)
        {
            _renderer = renderer;
            _originalSprite = originalSprite;
        }

        public override bool ApplyOverride(Texture2D replacement, out string error)
        {
            error = string.Empty;
            var sprite = CreateSpriteFromOriginal(replacement, _originalSprite);
            try
            {
                _renderer.sprite = sprite;
                DestroyReplacementSprite();
                _replacementSprite = sprite;
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Object.Destroy(sprite);
                error = ex.Message;
                return false;
            }
        }

        public override bool RestoreOriginal(out string error)
        {
            error = string.Empty;
            try
            {
                _renderer.sprite = _originalSprite;
                DestroyReplacementSprite();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void DestroyReplacementSprite()
        {
            if (_replacementSprite != null)
            {
                UnityEngine.Object.Destroy(_replacementSprite);
                _replacementSprite = null;
            }
        }
    }

    private sealed class SpriteSubregionTextureTarget : TextureTargetBase
    {
        private readonly PropertyInfo? _spriteProperty;
        private readonly SpriteRenderer? _renderer;
        private readonly Sprite _originalSprite;
        private readonly Rect _textureRect;
        private Sprite? _replacementSprite;

        public SpriteSubregionTextureTarget(
            string targetId,
            Component component,
            PropertyInfo? spriteProperty,
            SpriteRenderer? renderer,
            Sprite originalSprite,
            Rect textureRect)
            : base(targetId, component, originalSprite.texture)
        {
            _spriteProperty = spriteProperty;
            _renderer = renderer;
            _originalSprite = originalSprite;
            _textureRect = textureRect;
        }

        public override string TextureName => string.IsNullOrWhiteSpace(_originalSprite.name)
            ? base.TextureName
            : _originalSprite.name;

        public override int Width => Math.Max(1, (int)Math.Round(_textureRect.width));

        public override int Height => Math.Max(1, (int)Math.Round(_textureRect.height));

        public override bool TryCapturePng(out byte[] pngBytes, out string error)
        {
            return CropSpriteTextureRegion(SourceTexture, _textureRect, out pngBytes, out error);
        }

        public override bool ApplyOverride(Texture2D replacement, out string error)
        {
            var sprite = CreateSpriteFromOriginal(replacement, _originalSprite);
            if (SetSprite(sprite, out error))
            {
                DestroyReplacementSprite();
                _replacementSprite = sprite;
                return true;
            }

            UnityEngine.Object.Destroy(sprite);
            return false;
        }

        public override bool RestoreOriginal(out string error)
        {
            if (!SetSprite(_originalSprite, out error))
            {
                return false;
            }

            DestroyReplacementSprite();
            return true;
        }

        private bool SetSprite(Sprite sprite, out string error)
        {
            error = string.Empty;
            try
            {
                if (_renderer != null)
                {
                    _renderer.sprite = sprite;
                    return true;
                }

                if (_spriteProperty == null)
                {
                    error = "Sprite property is null.";
                    return false;
                }

                _spriteProperty.SetValue(Component, sprite, null);
                MarkDirty(Component);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void DestroyReplacementSprite()
        {
            if (_replacementSprite != null)
            {
                UnityEngine.Object.Destroy(_replacementSprite);
                _replacementSprite = null;
            }
        }
    }

    private sealed class RendererTextureTarget : TextureTargetBase
    {
        private readonly Renderer _renderer;

        public RendererTextureTarget(string targetId, Renderer renderer, Texture originalTexture)
            : base(targetId, renderer, originalTexture)
        {
            _renderer = renderer;
        }

        public override bool ApplyOverride(Texture2D replacement, out string error)
        {
            return SetTexture(replacement, out error);
        }

        public override bool RestoreOriginal(out string error)
        {
            return SetTexture(SourceTexture, out error);
        }

        private bool SetTexture(Texture texture, out string error)
        {
            error = string.Empty;
            try
            {
                var renderer = _renderer;
                if (renderer.sharedMaterial == null)
                {
                    error = "Renderer sharedMaterial is null.";
                    return false;
                }

                renderer.sharedMaterial.mainTexture = texture;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private static Sprite CreateSpriteFromOriginal(Texture2D replacement, Sprite original)
    {
        var rect = new Rect(0, 0, replacement.width, replacement.height);
        var pivot = original.pivot;
        var normalizedPivot = new Vector2(
            original.rect.width <= 0 ? 0.5f : pivot.x / original.rect.width,
            original.rect.height <= 0 ? 0.5f : pivot.y / original.rect.height);
        return Sprite.Create(
            replacement,
            rect,
            normalizedPivot,
            original.pixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            original.border);
    }
}
