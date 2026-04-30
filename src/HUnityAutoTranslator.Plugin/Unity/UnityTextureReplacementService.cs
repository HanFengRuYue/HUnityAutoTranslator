using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx.Logging;
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
    private const int MaxTextureScanTargetsPerTick = 8;
    private const int MaxTextureScanSliceMilliseconds = 8;

    private readonly TextureOverrideStore _store;
    private readonly TextureCatalogStore _catalogStore;
    private readonly Func<string?> _gameTitleProvider;
    private readonly ManualLogSource _logger;
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, RuntimeTextureRecord> _records = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UnityEngine.Object> _originalAssets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _replacementTextures = new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _scannedUtc;
    private IReadOnlyList<string> _lastErrors = Array.Empty<string>();
    private TextureScanSession? _scanSession;
    private string _scanStatusMessage = "空闲";
    private bool _startupScanAttempted;
    private bool _hasPersistedOverrides;
    private string? _lastAppliedSceneName;
    private bool _disposed;

    public UnityTextureReplacementService(
        TextureOverrideStore store,
        TextureCatalogStore catalogStore,
        Func<string?> gameTitleProvider,
        ManualLogSource logger)
    {
        _store = store;
        _catalogStore = catalogStore;
        _gameTitleProvider = gameTitleProvider;
        _logger = logger;
        _hasPersistedOverrides = _store.OverrideCount > 0;
    }

    public void Tick()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            action();
        }

        ProcessScanSlice();

        var currentSceneName = ActiveSceneName();
        if (!_startupScanAttempted)
        {
            _startupScanAttempted = true;
            _hasPersistedOverrides = _store.OverrideCount > 0;
            _lastAppliedSceneName = currentSceneName;
            if (_hasPersistedOverrides)
            {
                StartScanOnMainThread(applyOverrides: true, "正在扫描当前场景并应用已导入贴图。");
            }
            return;
        }

        if (_hasPersistedOverrides &&
            !string.Equals(_lastAppliedSceneName, currentSceneName, StringComparison.Ordinal))
        {
            _lastAppliedSceneName = currentSceneName;
            StartScanOnMainThread(applyOverrides: true, "场景变化，正在扫描并应用已导入贴图。");
        }
    }

    public Task<TextureScanResult> RequestScanAsync()
    {
        return EnqueueOnMainThread(() => StartScanOnMainThread(applyOverrides: true, "正在扫描贴图。"));
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

    public TextureCatalogPage GetCatalog(TextureCatalogQuery query)
    {
        var result = _catalogStore.Query(query, _store.LoadIndex());
        return new TextureCatalogPage(
            _scannedUtc,
            result.TotalCount,
            result.ReferenceCount,
            _store.OverrideCount,
            result.Items,
            _lastErrors,
            result.TotalCount,
            result.FilteredCount,
            result.Offset,
            result.Limit,
            result.Scenes,
            GetScanStatus());
    }

    public bool TryGetSourceImage(string sourceHash, out byte[] bytes)
    {
        return _catalogStore.TryReadSourceBytes(sourceHash, out bytes);
    }

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

    private TextureScanResult StartScanOnMainThread(bool applyOverrides, string message)
    {
        if (_scanSession != null)
        {
            return BuildScanResult("贴图扫描已在进行中。");
        }

        var errors = new List<string>();
        _scanSession = new TextureScanSession(
            EnumerateTargets(errors).GetEnumerator(),
            applyOverrides,
            DateTimeOffset.UtcNow,
            errors);
        _scanStatusMessage = message;
        _lastErrors = Array.Empty<string>();
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
        if (!TryEncodeTexture(target.SourceTexture, out var pngBytes, out var error))
        {
            AddError(session.Errors, $"跳过贴图 {target.TextureName}：{error}");
            return;
        }

        var hash = ComputeHash(pngBytes);
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
                    fileName,
                    pngBytes);
                _records.Add(hash, record);
            }

            record.AddTarget(target);
        }

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

        var applyError = string.Empty;
        if (session.ApplyOverrides && TryApplyOverrideToTarget(record, target, out applyError))
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
        _catalogStore.Save();
        _scannedUtc = DateTimeOffset.UtcNow;
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
            return TextureCatalogScanStatus.Idle(_scannedUtc);
        }

        return new TextureCatalogScanStatus(
            true,
            _scanStatusMessage,
            session.StartedUtc,
            null,
            session.ProcessedTargets,
            session.DiscoveredHashes.Count,
            session.DiscoveredReferenceCount);
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
            status.IsScanning,
            message);
    }

    private int ApplyOverridesToKnownTargets()
    {
        var applied = 0;
        foreach (var record in _records.Values.ToArray())
        {
            if (!_store.TryReadOverrideBytes(record.SourceHash, out var bytes))
            {
                continue;
            }

            if (!TryGetReplacementTexture(record, bytes, out var replacement, out var error))
            {
                _logger.LogWarning($"贴图覆盖加载失败：{record.TextureName}，{error}");
                continue;
            }

            foreach (var target in record.Targets.ToArray())
            {
                if (!target.IsAlive)
                {
                    continue;
                }

                if (target.ApplyOverride(replacement, out var applyError))
                {
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

    private bool TryApplyOverrideToTarget(RuntimeTextureRecord record, ITextureTarget target, out string error)
    {
        error = string.Empty;
        if (!_store.TryReadOverrideBytes(record.SourceHash, out var bytes))
        {
            return false;
        }

        if (!TryGetReplacementTexture(record, bytes, out var replacement, out var loadError))
        {
            error = $"贴图覆盖加载失败：{record.TextureName}，{loadError}";
            return false;
        }

        if (target.ApplyOverride(replacement, out var applyError))
        {
            return true;
        }

        error = $"贴图覆盖应用失败：{target.ComponentType}，{applyError}";
        return false;
    }

    private int RestoreKnownTargets()
    {
        var restored = 0;
        foreach (var target in _records.Values.SelectMany(record => record.Targets).ToArray())
        {
            if (!target.IsAlive)
            {
                continue;
            }

            if (target.RestoreOriginal(out var error))
            {
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
        byte[] bytes,
        out Texture2D replacement,
        out string error)
    {
        replacement = null!;
        error = string.Empty;

        if (_replacementTextures.TryGetValue(record.SourceHash, out var cached) && cached != null)
        {
            replacement = cached;
            return true;
        }

        var texture = new Texture2D(2, 2, UnityEngine.TextureFormat.RGBA32, false);
        if (!ImageConversion.LoadImage(texture, bytes, markNonReadable: false))
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
        _replacementTextures[record.SourceHash] = texture;
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

            if (sprite == null || sprite.texture == null || !HasValidSize(sprite.texture))
            {
                continue;
            }

            if (!IsFullTextureSprite(sprite))
            {
                AddError(errors, $"跳过图集子区域贴图：{BuildHierarchyPath(component.transform)}。");
                continue;
            }

            var targetId = TargetId("image", component);
            var original = RememberOriginalAsset(targetId, sprite);
            if (original is Sprite originalSprite && originalSprite.texture != null)
            {
                yield return new ImageSpriteTarget(targetId, component, spriteProperty, originalSprite);
            }
        }
    }

    private IEnumerable<ITextureTarget> EnumerateSpriteRendererTargets(List<string> errors)
    {
        foreach (var renderer in UnityObjectFinder.FindObjects(typeof(SpriteRenderer)).OfType<SpriteRenderer>())
        {
            var sprite = renderer.sprite;
            if (sprite == null || sprite.texture == null || !HasValidSize(sprite.texture))
            {
                continue;
            }

            if (!IsFullTextureSprite(sprite))
            {
                AddError(errors, $"跳过 SpriteRenderer 图集子区域贴图：{BuildHierarchyPath(renderer.transform)}。");
                continue;
            }

            var targetId = TargetId("sprite-renderer", renderer);
            var original = RememberOriginalAsset(targetId, sprite);
            if (original is Sprite originalSprite && originalSprite.texture != null)
            {
                yield return new SpriteRendererTextureTarget(targetId, renderer, originalSprite);
            }
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

    private static bool IsFullTextureSprite(Sprite sprite)
    {
        var rect = sprite.textureRect;
        return Math.Abs(rect.x) < 0.01f &&
            Math.Abs(rect.y) < 0.01f &&
            Math.Abs(rect.width - sprite.texture.width) < 0.01f &&
            Math.Abs(rect.height - sprite.texture.height) < 0.01f;
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

    private sealed class TextureScanSession
    {
        public TextureScanSession(
            IEnumerator<ITextureTarget> targets,
            bool applyOverrides,
            DateTimeOffset startedUtc,
            List<string> errors)
        {
            Targets = targets;
            ApplyOverrides = applyOverrides;
            StartedUtc = startedUtc;
            Errors = errors;
        }

        public IEnumerator<ITextureTarget> Targets { get; }

        public bool ApplyOverrides { get; }

        public DateTimeOffset StartedUtc { get; }

        public List<string> Errors { get; }

        public HashSet<string> DiscoveredHashes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int ProcessedTargets { get; set; }

        public int DiscoveredReferenceCount { get; set; }

        public int AppliedOverrideCount { get; set; }
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
            string fileName,
            byte[] pngBytes)
        {
            SourceHash = sourceHash;
            TextureName = textureName;
            Width = width;
            Height = height;
            Format = format;
            FileName = fileName;
            PngBytes = pngBytes;
        }

        public string SourceHash { get; }

        public string TextureName { get; }

        public int Width { get; }

        public int Height { get; }

        public string Format { get; }

        public string FileName { get; }

        public byte[] PngBytes { get; }

        public IReadOnlyList<ITextureTarget> Targets => _targets.Values.ToArray();

        public void AddTarget(ITextureTarget target)
        {
            _targets[target.TargetId] = target;
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

        public string TextureName => UnityTextureReplacementService.TextureName(SourceTexture);

        public int Width => SourceTexture.width;

        public int Height => SourceTexture.height;

        public string Format => UnityTextureReplacementService.TextureFormat(SourceTexture);

        public string SceneName => UnityTextureReplacementService.SceneName(Component);

        public string HierarchyPath => BuildHierarchyPath(Component.transform);

        public string ComponentType => Component.GetType().FullName ?? Component.GetType().Name;

        public bool IsAlive => Component != null;

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
                renderer.material.mainTexture = texture;
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
