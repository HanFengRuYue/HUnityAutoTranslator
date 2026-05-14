using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Core.Dispatching;
using HUnityAutoTranslator.Core.Glossary;
using HUnityAutoTranslator.Core.Pipeline;
using HUnityAutoTranslator.Core.Prompts;
using HUnityAutoTranslator.Core.Providers;
using HUnityAutoTranslator.Core.Queueing;
using HUnityAutoTranslator.Core.Text;
using HUnityAutoTranslator.Core.Textures;
using HUnityAutoTranslator.Plugin.Capture;
using HUnityAutoTranslator.Plugin.Hotkeys;
using HUnityAutoTranslator.Plugin.Unity;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HUnityAutoTranslator.Plugin;

internal sealed class SelfCheckService
{
    private const string ImguiHarmonyId = "com.hanfeng.hunityautotranslator.imgui";
    private const int MainThreadProbeTimeoutMilliseconds = 2000;
    private static readonly string[] TmpTextTypeNames =
    {
        "TMPro.TMP_Text, Unity.TextMeshPro",
        "TMPro.TMP_Text, Assembly-CSharp",
        "TMPro.TMP_Text, Unity.TextMeshProModule"
    };

    private readonly ControlPanelService _controlPanel;
    private readonly ITranslationCache _cache;
    private readonly IGlossaryStore _glossary;
    private readonly TranslationJobQueue _queue;
    private readonly ResultDispatcher _dispatcher;
    private readonly UnityMainThreadResultApplier _resultApplier;
    private readonly UnityTextureReplacementService _textureReplacement;
    private readonly LlamaCppServerManager? _llamaCppServer;
    private readonly string _pluginDirectory;
    private readonly string _dataDirectory;
    private readonly Func<string> _controlPanelUrlProvider;
    private readonly Func<MemoryDiagnosticsSnapshot> _memoryDiagnosticsProvider;
    private readonly ManualLogSource _logger;
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly string _lastReportPath;
    private readonly object _gate = new();
    private SelfCheckReport _latestReport;
    private int _isRunning;

    public SelfCheckService(
        ControlPanelService controlPanel,
        ITranslationCache cache,
        IGlossaryStore glossary,
        TranslationJobQueue queue,
        ResultDispatcher dispatcher,
        UnityMainThreadResultApplier resultApplier,
        UnityTextureReplacementService textureReplacement,
        LlamaCppServerManager? llamaCppServer,
        string pluginDirectory,
        string dataDirectory,
        Func<string> controlPanelUrlProvider,
        Func<MemoryDiagnosticsSnapshot> memoryDiagnosticsProvider,
        ManualLogSource logger)
    {
        _controlPanel = controlPanel;
        _cache = cache;
        _glossary = glossary;
        _queue = queue;
        _dispatcher = dispatcher;
        _resultApplier = resultApplier;
        _textureReplacement = textureReplacement;
        _llamaCppServer = llamaCppServer;
        _pluginDirectory = pluginDirectory;
        _dataDirectory = dataDirectory;
        _controlPanelUrlProvider = controlPanelUrlProvider;
        _memoryDiagnosticsProvider = memoryDiagnosticsProvider;
        _logger = logger;
        _lastReportPath = Path.Combine(dataDirectory, "self-check-last.json");
        _latestReport = LoadLastReport();
    }

    public SelfCheckReport GetLatestReport()
    {
        lock (_gate)
        {
            return _latestReport;
        }
    }

    public SelfCheckReport StartAutomaticAsync()
    {
        return StartRun("启动自动自检");
    }

    public SelfCheckReport StartManualAsync()
    {
        return StartRun("手动自检");
    }

    public void Tick()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            action();
        }
    }

    private SelfCheckReport StartRun(string trigger)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            return GetLatestReport();
        }

        var startedUtc = DateTimeOffset.UtcNow;
        var running = SelfCheckReport.Running(startedUtc) with
        {
            Message = trigger + "正在运行。"
        };
        SetLatest(running, persist: false);
        _ = Task.Run(() => RunAsync(startedUtc, trigger));
        return running;
    }

    private async Task RunAsync(DateTimeOffset startedUtc, string trigger)
    {
        var items = new List<SelfCheckItem>();
        try
        {
            items.AddRange(await CheckRuntimeEnvironmentAsync().ConfigureAwait(false));
            items.AddRange(await CheckTextCaptureAsync().ConfigureAwait(false));
            items.Add(CheckTranslationPipeline());
            items.Add(CheckLiveStorage());
            items.Add(CheckTemporaryStorage());
            items.Add(await CheckFontsAndWritebackAsync().ConfigureAwait(false));
            items.Add(await CheckTexturesAsync().ConfigureAwait(false));
            items.AddRange(CheckProviderConfiguration());
            items.AddRange(CheckLlamaCppLocalFiles());
            var completed = SelfCheckReport.Completed(startedUtc, DateTimeOffset.UtcNow, items) with
            {
                Message = $"{trigger}完成。"
            };
            SetLatest(completed, persist: true);
            LogSummary(completed);
        }
        catch (Exception ex)
        {
            var failed = SelfCheckReport.Failed(startedUtc, DateTimeOffset.UtcNow, ex.Message, items);
            SetLatest(failed, persist: true);
            _logger.LogWarning($"本地自检异常中止：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private async Task<IReadOnlyList<SelfCheckItem>> CheckRuntimeEnvironmentAsync()
    {
        var items = new List<SelfCheckItem>();
        items.Add(Measure("runtime.flavor", "运行环境", "插件运行时", () =>
        {
#if HUNITY_IL2CPP
            const string flavor = "BepInEx 6 / Unity IL2CPP";
#elif HUNITY_BEPINEX5
            const string flavor = "BepInEx 5 / Unity Mono";
#else
            const string flavor = "BepInEx 6 / Unity Mono";
#endif
            return SelfCheckItem.Ok("runtime.flavor", "运行环境", "插件运行时", flavor, "确认游戏安装的 BepInEx 包与插件包匹配。", 0);
        }));
        items.Add(Measure("runtime.bepinex-version", "运行环境", "BepInEx 版本", () =>
        {
            var host = BepInExRuntimeInfo.GetHostVersionString();
            var expected = BepInExRuntimeInfo.GetExpectedVersionString();
            var expectedText = string.IsNullOrWhiteSpace(expected) ? "未知" : expected;
            if (string.IsNullOrWhiteSpace(host))
            {
                return SelfCheckItem.Info(
                    "runtime.bepinex-version",
                    "运行环境",
                    "BepInEx 版本",
                    "无法读取宿主 BepInEx 版本",
                    $"插件构建基线：{expectedText}",
                    "如果插件无法加载，请确认安装的是最新 BepInEx Bleeding Edge 构建。",
                    0);
            }

            if (BepInExRuntimeInfo.IsHostOlderThanExpected(host, expected, out var detail))
            {
                return SelfCheckItem.Warning(
                    "runtime.bepinex-version",
                    "运行环境",
                    "BepInEx 版本",
                    $"宿主 BepInEx 版本低于插件构建基线。{detail}",
                    "请将 BepInEx 更新到最新 Bleeding Edge 构建（be.755 或更新）；过旧的 BepInEx 在最新 Unity（含 Unity 6 IL2CPP）上可能无法正确加载插件。",
                    0);
            }

            return SelfCheckItem.Ok(
                "runtime.bepinex-version",
                "运行环境",
                "BepInEx 版本",
                $"宿主：{host}；构建基线：{expectedText}",
                "无需处理。",
                0);
        }));
        items.Add(Measure("runtime.plugin-directory", "运行环境", "插件目录", () =>
            Directory.Exists(_pluginDirectory)
                ? SelfCheckItem.Ok("runtime.plugin-directory", "运行环境", "插件目录", _pluginDirectory, "无需处理。", 0)
                : SelfCheckItem.Error("runtime.plugin-directory", "运行环境", "插件目录", $"目录不存在：{_pluginDirectory}", "检查插件是否完整解压到 BepInEx/plugins/HUnityAutoTranslator。", 0)));
        items.Add(Measure("runtime.data-directory", "运行环境", "配置目录", () =>
        {
            Directory.CreateDirectory(_dataDirectory);
            var probePath = Path.Combine(_dataDirectory, "self-check-write.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);
            return SelfCheckItem.Ok("runtime.data-directory", "运行环境", "配置目录", _dataDirectory, "无需处理。", 0);
        }));
        items.Add(await RunMainThreadProbeAsync("runtime.unity-scene", "运行环境", "Unity 场景访问", () =>
        {
            var scene = SceneManager.GetActiveScene();
            var name = string.IsNullOrWhiteSpace(scene.name) ? "(未命名场景)" : scene.name;
            return SelfCheckItem.Ok("runtime.unity-scene", "运行环境", "Unity 场景访问", $"当前场景：{name}", "无需处理。", 0);
        }).ConfigureAwait(false));
        items.Add(Measure("runtime.http", "运行环境", "本机控制面板", () =>
        {
            var url = _controlPanelUrlProvider();
            if (string.IsNullOrWhiteSpace(url))
            {
                return SelfCheckItem.Warning("runtime.http", "运行环境", "本机控制面板", "控制面板地址尚未生成。", "确认 LocalHttpServer 已成功启动。", 0);
            }

            var loopback = url.StartsWith("http://127.0.0.1:", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("http://localhost:", StringComparison.OrdinalIgnoreCase);
            return loopback
                ? SelfCheckItem.Ok("runtime.http", "运行环境", "本机控制面板", $"监听地址：{url}", "无需处理。", 0)
                : SelfCheckItem.Warning("runtime.http", "运行环境", "本机控制面板", $"监听地址不是 loopback：{url}", "控制面板应只监听本机地址。", 0);
        }));
        items.Add(Measure("runtime.memory", "运行环境", "插件内存快照", () =>
        {
            var snapshot = _memoryDiagnosticsProvider();
            var evidence =
                $"托管内存：{FormatBytes(snapshot.ManagedMemoryBytes)}；" +
                $"Unity 已分配：{FormatBytes(snapshot.UnityAllocatedMemoryBytes)}；" +
                $"队列：{snapshot.QueueCount}；写回：{snapshot.WritebackQueueCount}；" +
                $"字体缓存：{snapshot.FontCacheCount}/{snapshot.TmpFontAssetCacheCount}；" +
                $"纹理记录：{snapshot.TextureRecordCount}；替换纹理：{snapshot.ReplacementTextureCount}。";
            return SelfCheckItem.Info(
                "runtime.memory",
                "运行环境",
                "插件内存快照",
                "用于排查内存占用",
                evidence,
                "如果游戏进程内存持续上涨，请对比启动、空闲、切场景和贴图导入后的该项数值。",
                0);
        }));
        return items;
    }

    private async Task<IReadOnlyList<SelfCheckItem>> CheckTextCaptureAsync()
    {
        var config = _controlPanel.GetConfig();
        return new[]
        {
            await CheckReflectedTextTypeAsync("capture.ugui", "UGUI", "UnityEngine.UI.Text, UnityEngine.UI", config.EnableUgui).ConfigureAwait(false),
            await CheckReflectedTextTypeAsync("capture.tmp", "TextMeshPro", TmpTextTypeNames, config.EnableTmp).ConfigureAwait(false),
            CheckImguiHooks(config.EnableImgui)
        };
    }

    private async Task<SelfCheckItem> CheckReflectedTextTypeAsync(string id, string name, string typeName, bool enabled)
    {
        return await CheckReflectedTextTypeAsync(id, name, new[] { typeName }, enabled).ConfigureAwait(false);
    }

    private async Task<SelfCheckItem> CheckReflectedTextTypeAsync(string id, string name, IReadOnlyList<string> typeNames, bool enabled)
    {
        if (!enabled)
        {
            return SelfCheckItem.Skipped(id, "文本采集", name, $"{name} 采集已在设置中关闭。", "需要采集该类文本时，在插件设置中重新启用。", 0);
        }

        var type = typeNames
            .Select(Type.GetType)
            .FirstOrDefault(candidate => candidate != null);
        if (type == null)
        {
            return SelfCheckItem.Warning(id, "文本采集", name, $"未发现类型：{string.Join(", ", typeNames)}", $"如果该游戏使用 {name} 文本，请检查相关程序集是否存在。", 0);
        }

        var textProperty = type.GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
        if (textProperty == null)
        {
            return SelfCheckItem.Error(id, "文本采集", name, $"类型 {type.FullName} 缺少 public text 属性。", "该游戏的文本组件接口可能不兼容，需要适配新的读取方式。", 0);
        }

        return await RunMainThreadProbeAsync(id, "文本采集", name, () =>
        {
            var objects = UnityObjectFinder.FindObjects(type);
            var severity = objects.Length == 0 ? SelfCheckSeverity.Info : SelfCheckSeverity.Ok;
            return SelfCheckItem.Create(
                id,
                "文本采集",
                name,
                severity,
                objects.Length == 0 ? "未发现当前目标" : "正常",
                $"类型：{type.FullName}；当前场景目标数：{objects.Length}",
                objects.Length == 0 ? "当前场景可能没有该类文本；切换到含 UI 的场景后可重新自检。" : "无需处理。",
                0);
        }).ConfigureAwait(false);
    }

    private SelfCheckItem CheckImguiHooks(bool enabled)
    {
        return Measure("capture.imgui", "文本采集", "IMGUI", () =>
        {
            if (!enabled)
            {
                return SelfCheckItem.Skipped("capture.imgui", "文本采集", "IMGUI", "IMGUI 采集已在设置中关闭。", "需要采集旧式 GUI 文本时，在插件设置中重新启用。", 0);
            }

            var methods = typeof(UnityEngine.GUI).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Concat(typeof(UnityEngine.GUILayout).GetMethods(BindingFlags.Public | BindingFlags.Static))
                .Where(IsSupportedImguiTextMethod)
                .Distinct()
                .ToArray();
            var patched = methods.Count(method =>
                Harmony.GetPatchInfo(method)?.Prefixes.Any(prefix => prefix.owner == ImguiHarmonyId) == true);
            if (methods.Length == 0)
            {
                return SelfCheckItem.Warning("capture.imgui", "文本采集", "IMGUI", "未找到可支持的 GUI/GUILayout 字符串方法。", "该 Unity 版本的 IMGUI API 可能不同，需要补充适配。", 0);
            }

            return SelfCheckItem.Create(
                "capture.imgui",
                "文本采集",
                "IMGUI",
                patched > 0 ? SelfCheckSeverity.Ok : SelfCheckSeverity.Warning,
                patched > 0 ? "正常" : "未确认钩子",
                $"可检查方法数：{methods.Length}；已安装前缀数：{patched}",
                patched > 0 ? "无需处理。" : "如果 IMGUI 文本无法翻译，请查看启动日志里的 Harmony 安装警告。",
                0);
        });
    }

    private SelfCheckItem CheckTranslationPipeline()
    {
        return Measure("pipeline.local", "翻译链路", "内部处理链路", () =>
        {
            var config = _controlPanel.GetConfig();
            var cache = new MemoryTranslationCache();
            var queue = new TranslationJobQueue();
            var glossary = new MemoryGlossaryStore();
            glossary.UpsertManual(GlossaryTerm.CreateManual("Start", "开始", config.TargetLanguage, null));
            var pipeline = new TextPipeline(cache, queue, config, new ControlPanelMetrics(), glossary);
            var ignored = pipeline.Process(new CapturedText("self-check-ignored", "12345", true));
            var queued = pipeline.Process(new CapturedText("self-check-queued", "Self Check Sample", true));
            var key = TranslationCacheKey.Create("Cached Sample", config.TargetLanguage, config.Provider, TextPipeline.GetPromptPolicyVersion(config));
            cache.Set(key, "缓存样例", TranslationCacheContext.Empty);
            var cached = pipeline.Process(new CapturedText("self-check-cached", "Cached Sample", true));
            var outputValidation = TranslationOutputValidator.ValidateSingle("Start", "开始", requireSameRichTextTags: true);
            var prompt = PromptBuilder.BuildDefaultSystemPrompt(config.TargetLanguage, config.Style, config.GameTitle);
            var dispatcher = new ResultDispatcher();
            dispatcher.Publish(new TranslationResult("self-check", "Start", "开始", 1));
            var drained = dispatcher.Drain(1);

            var ok = ignored.Kind == PipelineDecisionKind.Ignored &&
                queued.Kind == PipelineDecisionKind.Queued &&
                queue.PendingCount == 1 &&
                cached.Kind == PipelineDecisionKind.UseCachedTranslation &&
                outputValidation.IsValid &&
                !string.IsNullOrWhiteSpace(prompt) &&
                drained.Count == 1;
            return ok
                ? SelfCheckItem.Ok("pipeline.local", "翻译链路", "内部处理链路", "过滤、缓存命中、入队、提示词、输出校验、结果分发均可本地调用；未创建真实翻译请求。", "无需处理。", 0)
                : SelfCheckItem.Error("pipeline.local", "翻译链路", "内部处理链路", "内部处理链路的本地样例未通过。", "检查 TextPipeline、缓存、队列和结果分发器。", 0);
        });
    }

    private SelfCheckItem CheckLiveStorage()
    {
        return Measure("storage.live", "本地存储", "运行缓存和术语库", () =>
        {
            var cacheCount = _cache.Count;
            var glossaryCount = _glossary.Count;
            return SelfCheckItem.Ok(
                "storage.live",
                "本地存储",
                "运行缓存和术语库",
                $"翻译缓存已译文：{cacheCount}；术语库：{glossaryCount}",
                "无需处理。",
                0);
        });
    }

    private SelfCheckItem CheckTemporaryStorage()
    {
        return Measure("storage.temporary", "本地存储", "隔离写入探针", () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "HUnityAutoTranslator-self-check-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempRoot);
                using var cache = new SqliteTranslationCache(Path.Combine(tempRoot, "translation-cache.sqlite"));
                using var glossary = new SqliteGlossaryStore(Path.Combine(tempRoot, "translation-glossary.sqlite"));
                var config = _controlPanel.GetConfig();
                var key = TranslationCacheKey.Create("Temporary Storage Sample", config.TargetLanguage, config.Provider, TextPipeline.GetPromptPolicyVersion(config));
                cache.Set(key, "临时存储样例", TranslationCacheContext.Empty);
                glossary.UpsertManual(GlossaryTerm.CreateManual("Temporary", "临时", config.TargetLanguage, null));
                var ok = cache.TryGet(key, TranslationCacheContext.Empty, out var translated) &&
                    translated == "临时存储样例" &&
                    glossary.Count == 1;
                return ok
                    ? SelfCheckItem.Ok("storage.temporary", "本地存储", "隔离写入探针", $"临时目录：{tempRoot}", "自检只写入临时目录，不改动真实翻译缓存。", 0)
                    : SelfCheckItem.Error("storage.temporary", "本地存储", "隔离写入探针", "临时 SQLite 读写结果不一致。", "检查 SQLite 运行库和目录权限。", 0);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        });
    }

    private async Task<SelfCheckItem> CheckFontsAndWritebackAsync()
    {
        return await RunMainThreadProbeAsync("font.writeback", "字体与写回", "字体和目标快照", () =>
        {
            var config = _controlPanel.GetConfig();
            var targets = _resultApplier.SnapshotTargets();
            var tmpType = TmpTextTypeNames.Select(Type.GetType).FirstOrDefault(type => type != null);
            var cjkFonts = new[]
            {
                @"C:\Windows\Fonts\simhei.ttf",
                @"C:\Windows\Fonts\Deng.ttf",
                @"C:\Windows\Fonts\simsun.ttc",
                @"C:\Windows\Fonts\NotoSansSC-VF.ttf"
            }.Where(File.Exists).ToArray();
            var hotkeysOk = RuntimeHotkey.TryParse(config.OpenControlPanelHotkey, out _) &&
                RuntimeHotkey.TryParse(config.ToggleTranslationHotkey, out _) &&
                RuntimeHotkey.TryParse(config.ForceScanHotkey, out _) &&
                RuntimeHotkey.TryParse(config.ToggleFontHotkey, out _);
            if (!hotkeysOk)
            {
                return SelfCheckItem.Warning("font.writeback", "字体与写回", "字体和目标快照", "存在无法解析的热键绑定。", "在插件设置页重新保存热键。", 0);
            }

            var evidence = $"已登记文本目标：{targets.Count}；TMP 字体类型：{(tmpType == null ? "未发现" : tmpType.FullName)}；候选 CJK 字体文件：{cjkFonts.Length}；字体替换：{config.EnableFontReplacement}";
            return cjkFonts.Length == 0 && config.EnableFontReplacement
                ? SelfCheckItem.Warning("font.writeback", "字体与写回", "字体和目标快照", evidence, "如果中文显示为方块，请手动选择可用中文字体。", 0)
                : SelfCheckItem.Ok("font.writeback", "字体与写回", "字体和目标快照", evidence, "无需处理。", 0);
        }).ConfigureAwait(false);
    }

    private async Task<SelfCheckItem> CheckTexturesAsync()
    {
        return await RunMainThreadProbeAsync("textures.local", "贴图功能", "贴图目录和当前场景", () =>
        {
            var catalog = _textureReplacement.GetCatalog(new TextureCatalogQuery(null, 0, 1));
            var catalogDirectory = Path.Combine(_dataDirectory, "texture-catalog");
            var overrideDirectory = Path.Combine(_dataDirectory, "texture-overrides");
            ProbeWritableDirectory(catalogDirectory);
            ProbeWritableDirectory(overrideDirectory);

            var dryRun = CountCurrentTextureTargetsDryRun();
            var evidence = $"目录贴图数：{catalog.TextureCount}；引用数：{catalog.ReferenceCount}；当前场景 dry-run 目标：{dryRun.TargetCount}；枚举错误：{dryRun.ErrorCount}；扫描状态：{catalog.ScanStatus.Message}；贴图目录：{catalogDirectory}；覆盖目录：{overrideDirectory}";
            if (dryRun.ErrorCount > 0)
            {
                return SelfCheckItem.Warning(
                    "textures.local",
                    "贴图功能",
                    "贴图目录和当前场景",
                    evidence + "；示例错误：" + dryRun.ErrorSummary,
                    "当前场景贴图枚举存在兼容异常；查看证据里的组件类型后再决定是否补充适配。",
                    0);
            }

            return SelfCheckItem.Ok("textures.local", "贴图功能", "贴图目录和当前场景", evidence, "自检不调用图片生成或视觉识别 API，也不生成或覆盖贴图。", 0);
        }).ConfigureAwait(false);
    }

    private IReadOnlyList<SelfCheckItem> CheckProviderConfiguration()
    {
        var items = new List<SelfCheckItem>();
        items.Add(Measure("provider.profiles", "AI 服务", "服务商配置", () =>
        {
            var state = _controlPanel.GetState(_queue.PendingCount, _cache.Count, _dispatcher.PendingCount);
            var profiles = state.ProviderProfiles ?? Array.Empty<ProviderProfileState>();
            if (profiles.Count == 0)
            {
                return SelfCheckItem.Warning("provider.profiles", "AI 服务", "服务商配置", "未配置 AI 翻译服务商。", "需要自动翻译时，请在 AI 翻译设置中添加服务商配置。", 0);
            }

            var enabled = profiles.Where(profile => profile.Enabled).ToArray();
            var problems = enabled
                .Where(profile => string.IsNullOrWhiteSpace(profile.Model) ||
                    (profile.Kind != ProviderKind.LlamaCpp && string.IsNullOrWhiteSpace(profile.Endpoint)) ||
                    (profile.Kind is ProviderKind.OpenAI or ProviderKind.DeepSeek && !profile.ApiKeyConfigured))
                .Select(profile => profile.Name)
                .ToArray();
            if (problems.Length > 0)
            {
                return SelfCheckItem.Warning("provider.profiles", "AI 服务", "服务商配置", $"配置不完整：{string.Join(", ", problems)}", "补齐模型、端点和必要的 API Key。", 0);
            }

            return SelfCheckItem.Ok("provider.profiles", "AI 服务", "服务商配置", $"配置总数：{profiles.Count}；启用：{enabled.Length}", "仅检查本地配置完整性，未连接外部服务。", 0);
        }));
        items.Add(Measure("provider.texture-profiles", "AI 服务", "贴图图片服务配置", () =>
        {
            var state = _controlPanel.GetState(_queue.PendingCount, _cache.Count, _dispatcher.PendingCount);
            var profiles = state.TextureImageProviderProfiles ?? Array.Empty<TextureImageProviderProfileState>();
            if (profiles.Count == 0)
            {
                return SelfCheckItem.Info("provider.texture-profiles", "AI 服务", "贴图图片服务配置", "未配置贴图图片服务。", "贴图文字翻译功能将不可用。", "需要贴图翻译时，请添加贴图图片服务配置。", 0);
            }

            var ready = profiles.Count(profile =>
                profile.Enabled &&
                profile.ApiKeyConfigured &&
                !string.IsNullOrWhiteSpace(profile.BaseUrl) &&
                !string.IsNullOrWhiteSpace(profile.EditEndpoint) &&
                !string.IsNullOrWhiteSpace(profile.ImageModel));
            return ready > 0
                ? SelfCheckItem.Ok("provider.texture-profiles", "AI 服务", "贴图图片服务配置", $"配置总数：{profiles.Count}；本地看起来可用：{ready}", "未调用贴图图片服务 API。", 0)
                : SelfCheckItem.Warning("provider.texture-profiles", "AI 服务", "贴图图片服务配置", $"配置总数：{profiles.Count}；没有完整启用项。", "补齐启用状态、模型、端点和 API Key。", 0);
        }));
        items.Add(SelfCheckItem.Skipped("provider.external-api", "AI 服务", "外部服务连接", "按自检策略跳过外部 API 调用。", "需要验证额度、模型列表或连通性时，请在 AI 设置页手动测试。", 0));
        return items;
    }

    private IReadOnlyList<SelfCheckItem> CheckLlamaCppLocalFiles()
    {
        var items = new List<SelfCheckItem>();
        items.Add(Measure("llamacpp.files", "llama.cpp", "本地后端文件", () =>
        {
            var config = _controlPanel.GetConfig();
            var status = _llamaCppServer?.GetStatus(config) ?? LlamaCppServerStatus.Error(config.LlamaCpp, string.Empty, "llama.cpp 管理器不可用。");
            var llamaDirectory = Path.Combine(_pluginDirectory, "llama.cpp");
            var bench = Path.Combine(llamaDirectory, "llama-bench.exe");
            var batchedBench = Path.Combine(llamaDirectory, "llama-batched-bench.exe");
            var evidence = $"安装：{status.Installed}；backend.json：{File.Exists(Path.Combine(llamaDirectory, "backend.json"))}；server：{File.Exists(status.ServerPath ?? string.Empty)}；bench：{File.Exists(bench)}；batched-bench：{File.Exists(batchedBench)}";
            return status.Installed
                ? SelfCheckItem.Ok("llamacpp.files", "llama.cpp", "本地后端文件", evidence, "自检只检查本地文件，不启动 llama.cpp。", 0)
                : SelfCheckItem.Info("llamacpp.files", "llama.cpp", "本地后端文件", "未安装 llama.cpp 后端包。", evidence, "只有使用本地模型时才需要安装后端包。", 0);
        }));
        items.Add(Measure("llamacpp.model", "llama.cpp", "GGUF 模型文件", () =>
        {
            var modelPath = _controlPanel.GetConfig().LlamaCpp.ModelPath;
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                return SelfCheckItem.Info("llamacpp.model", "llama.cpp", "GGUF 模型文件", "尚未选择 GGUF 模型。", "只有使用本地模型时才需要选择模型文件。", "在 AI 设置页选择模型文件。", 0);
            }

            return File.Exists(modelPath)
                ? SelfCheckItem.Ok("llamacpp.model", "llama.cpp", "GGUF 模型文件", $"模型文件存在：{modelPath}", "无需处理。", 0)
                : SelfCheckItem.Warning("llamacpp.model", "llama.cpp", "GGUF 模型文件", $"模型文件不存在：{modelPath}", "重新选择存在的 GGUF 模型文件。", 0);
        }));
        items.Add(SelfCheckItem.Skipped("llamacpp.runtime", "llama.cpp", "本地模型运行状态", "不启动 llama.cpp，不访问 /health，不运行 benchmark。", "需要运行验证时，请在 AI 设置页手动启动或基准测试。", 0));
        return items;
    }

    private static (int TargetCount, int ErrorCount, string ErrorSummary) CountCurrentTextureTargetsDryRun()
    {
        var errors = new List<string>();
        var rawImageType = Type.GetType("UnityEngine.UI.RawImage, UnityEngine.UI");
        var imageType = Type.GetType("UnityEngine.UI.Image, UnityEngine.UI");
        var count = CountTargetsWithUnityProperty(rawImageType, "texture", errors) +
            CountTargetsWithUnityProperty(imageType, "sprite", errors) +
            CountSpriteRendererTargets(errors) +
            CountRendererTextureTargets(errors);
        return (count, errors.Count, string.Join("；", errors.Take(3)));
    }

    private static int CountTargetsWithUnityProperty(Type? type, string propertyName, List<string> errors)
    {
        var property = type?.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (type == null || property == null)
        {
            return 0;
        }

        var count = 0;
        foreach (var obj in UnityObjectFinder.FindObjects(type))
        {
            try
            {
                var value = property.GetValue(obj, null);
                if (value is Texture texture && HasValidTextureSize(texture))
                {
                    count++;
                }
                else if (value is Sprite sprite && sprite.texture != null && HasValidTextureSize(sprite.texture))
                {
                    count++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{type.Name}.{propertyName}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return count;
    }

    private static int CountSpriteRendererTargets(List<string> errors)
    {
        var count = 0;
        foreach (var renderer in UnityObjectFinder.FindObjects(typeof(SpriteRenderer)).OfType<SpriteRenderer>())
        {
            try
            {
                if (renderer.sprite?.texture != null && HasValidTextureSize(renderer.sprite.texture))
                {
                    count++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"SpriteRenderer.sprite: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return count;
    }

    private static int CountRendererTextureTargets(List<string> errors)
    {
        var count = 0;
        foreach (var renderer in UnityObjectFinder.FindObjects(typeof(Renderer)).OfType<Renderer>())
        {
            if (renderer is SpriteRenderer)
            {
                continue;
            }

            try
            {
                var texture = renderer.sharedMaterial?.mainTexture;
                if (texture != null && HasValidTextureSize(texture))
                {
                    count++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Renderer.mainTexture: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return count;
    }

    private static bool HasValidTextureSize(Texture texture)
    {
        return texture.width > 0 && texture.height > 0;
    }

    private static void ProbeWritableDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, "self-check-write-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(probePath, "ok");
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }

    private SelfCheckItem Measure(string id, string category, string name, Func<SelfCheckItem> action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return action() with { DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds };
        }
        catch (Exception ex)
        {
            return SelfCheckItem.Error(
                id,
                category,
                name,
                $"{ex.GetType().Name}: {ex.Message}",
                "查看 BepInEx 日志并按该项证据定位兼容问题。",
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private async Task<SelfCheckItem> RunMainThreadProbeAsync(string id, string category, string name, Func<SelfCheckItem> action)
    {
        var stopwatch = Stopwatch.StartNew();
        var completion = new TaskCompletionSource<SelfCheckItem>(TaskCreationOptions.RunContinuationsAsynchronously);
        _mainThreadActions.Enqueue(() =>
        {
            try
            {
                completion.SetResult(action() with { DurationMilliseconds = stopwatch.Elapsed.TotalMilliseconds });
            }
            catch (Exception ex)
            {
                completion.SetResult(SelfCheckItem.Error(
                    id,
                    category,
                    name,
                    $"{ex.GetType().Name}: {ex.Message}",
                    "该项需要 Unity 主线程兼容；请查看游戏日志中的相关异常。",
                    stopwatch.Elapsed.TotalMilliseconds));
            }
        });

        var finished = await Task.WhenAny(completion.Task, Task.Delay(MainThreadProbeTimeoutMilliseconds)).ConfigureAwait(false);
        return finished == completion.Task
            ? await completion.Task.ConfigureAwait(false)
            : SelfCheckItem.Warning(id, category, name, "等待 Unity 主线程执行超时。", "确认游戏主循环仍在运行，然后手动重新自检。", stopwatch.Elapsed.TotalMilliseconds);
    }

    private SelfCheckReport LoadLastReport()
    {
        try
        {
            if (!File.Exists(_lastReportPath))
            {
                return SelfCheckReport.NotStarted();
            }

            return JsonConvert.DeserializeObject<SelfCheckReport>(File.ReadAllText(_lastReportPath)) ?? SelfCheckReport.NotStarted();
        }
        catch
        {
            return SelfCheckReport.NotStarted();
        }
    }

    private void SetLatest(SelfCheckReport report, bool persist)
    {
        lock (_gate)
        {
            _latestReport = report;
        }

        if (!persist)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_dataDirectory);
            File.WriteAllText(_lastReportPath, JsonConvert.SerializeObject(report, Formatting.Indented));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning($"保存本地自检报告失败：{ex.Message}");
        }
    }

    private void LogSummary(SelfCheckReport report)
    {
        if (report.ErrorCount > 0)
        {
            _logger.LogWarning($"本地自检完成：{report.ErrorCount} 个异常，{report.WarningCount} 个警告。");
            return;
        }

        if (report.WarningCount > 0)
        {
            _logger.LogWarning($"本地自检完成：{report.WarningCount} 个警告。");
            return;
        }

        _logger.LogInfo("本地自检完成：未发现阻断问题。");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "未知";
        }

        return $"{bytes / 1024d / 1024d:0.0} MB";
    }

    private static bool IsSupportedImguiTextMethod(MethodInfo method)
    {
        if (method.Name is not ("Label" or "Button" or "Toggle" or "TextField"))
        {
            return false;
        }

        return method.GetParameters().Any(parameter => parameter.Name == "text" && parameter.ParameterType == typeof(string));
    }

    private static void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch
        {
        }
    }
}
