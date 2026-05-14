using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using HUnityAutoTranslator.Core.Caching;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Toolbox.Core.Database;
using HUnityAutoTranslator.Toolbox.Core.Installation;
using Microsoft.Web.WebView2.Core;
using WinForms = System.Windows.Forms;

namespace HUnityAutoTranslator.Toolbox;

public partial class MainWindow : Window
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private bool _isShuttingDown;
    private readonly object _installRunLock = new();
    private InstallRunState? _activeInstallRun;
    private readonly InstallExecutor _installExecutor = new();

    public MainWindow()
    {
        InitializeComponent();
        EmbeddedAssetCatalog.UseAssembly(typeof(MainWindow).Assembly);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = GetWebViewUserDataFolder();
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
            await WebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
            try
            {
                WebView.CoreWebView2.Settings.IsNonClientRegionSupportEnabled = true;
            }
            catch (NotImplementedException)
            {
                // 老版本 WebView2 Runtime 不支持，自定义标题栏仍可渲染，仅拖动失效。
            }
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            WebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(BridgeScript).ConfigureAwait(true);
            WebView.NavigateToString(ToolboxHtml.Document);
        }
        catch (Exception ex)
        {
            WebView.Visibility = Visibility.Collapsed;
            Fallback.Visibility = Visibility.Visible;
            FallbackMessage.Text = "请确认系统已安装 Microsoft Edge WebView2 Runtime，然后重新打开工具箱。\n\n" + ex.Message;
        }
    }

    private static string GetWebViewUserDataFolder()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "HUnityAutoTranslator", "Toolbox", "WebView2");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(handle);
        source?.AddHook(WindowProc);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        PushWindowState();
    }

    private void PushWindowState()
    {
        var coreWebView = WebView?.CoreWebView2;
        if (coreWebView is null)
        {
            return;
        }

        var json = JsonSerializer.Serialize(new
        {
            type = "windowStateChanged",
            state = DescribeWindowState()
        }, JsonOptions);
        try
        {
            coreWebView.PostWebMessageAsJson(json);
        }
        catch
        {
            // WebView2 在关闭过程中可能拒绝消息，忽略以免阻塞窗口状态变更。
        }
    }

    private string DescribeWindowState()
    {
        return WindowState == WindowState.Maximized ? "maximized" : "normal";
    }

    private object? InvokeWindowAction(Action action)
    {
        Dispatcher.Invoke(action);
        return null;
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        base.OnClosed(e);

        try
        {
            WebView?.Dispose();
        }
        catch
        {
            // WebView2 disposal can race with native teardown; ignore so shutdown still completes.
        }

        Application.Current?.Shutdown();

        // WebView2 occasionally keeps background threads alive past Shutdown; force the process to exit.
        Environment.Exit(0);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        ToolboxBridgeRequest? request = null;
        try
        {
            request = JsonSerializer.Deserialize<ToolboxBridgeRequest>(e.WebMessageAsJson, JsonOptions);
            if (request == null || string.IsNullOrWhiteSpace(request.Id))
            {
                return;
            }

            var result = await Task.FromResult(HandleCommand(request)).ConfigureAwait(true);
            PostResponse(new ToolboxBridgeResponse(request.Id, true, result, null));
        }
        catch (Exception ex)
        {
            if (request != null)
            {
                PostResponse(new ToolboxBridgeResponse(request.Id, false, null, ex.Message));
            }
        }
    }

    private object? HandleCommand(ToolboxBridgeRequest request)
    {
        var payload = request.Payload;
        return request.Command switch
        {
            "getAppInfo" => new
            {
                Name = "HUnityAutoTranslator 工具箱",
                Version = typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "0.1.1"
            },
            "inspectGame" => GameInspector.Inspect(ReadString(payload, "gameRoot")),
            "pickGameDirectory" => PickGameDirectory(payload),
            "pickFile" => PickFile(payload),
            "pickFontFile" => PickFontFile(),
            "createInstallPlan" => CreateInstallPlan(payload),
            "executeInstallPlan" => ExecuteInstallPlan(payload),
            "cancelInstall" => CancelInstall(payload),
            "rollbackInstall" => RollbackInstall(payload),
            "getEmbeddedBundleInfo" => GetEmbeddedBundleInfo(),
            "loadPluginConfig" => LoadPluginConfig(payload),
            "savePluginConfig" => SavePluginConfig(payload),
            "queryTranslations" => QueryTranslations(payload),
            "getTranslationFilterOptions" => GetTranslationFilterOptions(payload),
            "updateTranslation" => UpdateTranslation(payload),
            "deleteTranslations" => DeleteTranslations(payload),
            "exportTranslations" => ExportTranslations(payload),
            "importTranslations" => ImportTranslations(payload),
            "runDatabaseMaintenance" => RunDatabaseMaintenance(payload),
            "windowMinimize" => InvokeWindowAction(() => WindowState = WindowState.Minimized),
            "windowMaximizeRestore" => InvokeWindowAction(() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
            "windowClose" => InvokeWindowAction(Close),
            "getWindowState" => DescribeWindowState(),
            _ => throw new InvalidOperationException("未知工具箱命令：" + request.Command)
        };
    }

    private static string PickGameDirectory(JsonElement payload)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            Description = "选择 Unity 游戏根目录",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        var initialDirectory = ReadString(payload, "initialDirectory");
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == WinForms.DialogResult.OK ? dialog.SelectedPath : string.Empty;
    }

    private static FontPickResult PickFontFile()
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = "选择替换字体文件",
            Filter = "字体文件 (*.ttf;*.otf)|*.ttf;*.otf|TrueType 字体 (*.ttf)|*.ttf|OpenType 字体 (*.otf)|*.otf",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return FontPickResult.Cancelled();
        }

        var path = dialog.FileName;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return FontPickResult.Error("未能读取所选字体文件路径。");
        }

        var fontName = FontNameReader.TryReadFamilyName(path, out var parsedName)
            ? parsedName
            : Path.GetFileNameWithoutExtension(path);
        return FontPickResult.Selected(path, fontName);
    }

    private static InstallPlan CreateInstallPlan(JsonElement payload)
    {
        var inspection = GameInspector.Inspect(ReadString(payload, "gameRoot"));
        var options = BuildInstallOptions(payload);
        return InstallPlanner.CreatePlan(inspection, options);
    }

    private static InstallPlanOptions BuildInstallOptions(JsonElement payload)
    {
        return new InstallPlanOptions(
            PackageVersion: ReadString(payload, "packageVersion", "0.1.1"),
            Mode: ReadEnum(payload, "mode", InstallMode.Full),
            IncludeLlamaCppBackend: ReadBool(payload, "includeLlamaCppBackend"),
            LlamaCppBackend: ReadEnum(payload, "llamaCppBackend", LlamaCppBackendKind.None),
            RuntimeOverride: ReadNullableEnum<ToolboxRuntimeKind>(payload, "runtimeOverride"),
            BepInExHandling: ReadEnum(payload, "bepInExHandling", BepInExHandling.Auto),
            BackupPolicy: ReadEnum(payload, "backupPolicy", BackupPolicy.Auto),
            CustomPluginDirectory: ReadNullableString(payload, "customPluginDirectory"),
            CustomConfigDirectory: ReadNullableString(payload, "customConfigDirectory"),
            CustomBackupDirectory: ReadNullableString(payload, "customBackupDirectory"),
            CustomPluginZipPath: ReadNullableString(payload, "customPluginZipPath"),
            CustomBepInExZipPath: ReadNullableString(payload, "customBepInExZipPath"),
            CustomLlamaCppZipPath: ReadNullableString(payload, "customLlamaCppZipPath"),
            CustomUnityLibraryZipPath: ReadNullableString(payload, "customUnityLibraryZipPath"),
            UnityVersionOverride: ReadNullableString(payload, "unityVersionOverride"),
            DryRun: ReadBool(payload, "dryRun"),
            ForceReinstall: ReadBool(payload, "forceReinstall"),
            SkipPostInstallVerification: ReadBool(payload, "skipPostInstallVerification"));
    }

    private object ExecuteInstallPlan(JsonElement payload)
    {
        var inspection = GameInspector.Inspect(ReadString(payload, "gameRoot"));
        var options = BuildInstallOptions(payload);
        var plan = InstallPlanner.CreatePlan(inspection, options);

        var runId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();

        lock (_installRunLock)
        {
            if (_activeInstallRun is not null)
            {
                cts.Dispose();
                throw new InvalidOperationException("已有安装任务正在进行,请等其完成或取消后再试。");
            }
            _activeInstallRun = new InstallRunState(runId, cts, null);
        }

        var progress = new Progress<InstallProgress>(p => PostInstallProgress(runId, p));
        var task = Task.Run(async () =>
        {
            try
            {
                var result = await _installExecutor.ExecuteAsync(plan, progress, cts.Token).ConfigureAwait(false);
                if (result.FinalStage == InstallStage.Cancelled)
                {
                    PostInstallEvent("installCancelled", new
                    {
                        runId,
                        operationIndex = result.FailedOperationIndex,
                        backupDirectory = result.BackupDirectory
                    });
                }
                else if (result.Succeeded)
                {
                    PostInstallEvent("installCompleted", new { runId, result });
                }
                else
                {
                    PostInstallEvent("installFailed", new
                    {
                        runId,
                        error = result.Message,
                        stage = result.FinalStage.ToString(),
                        operationIndex = result.FailedOperationIndex,
                        backupDirectory = result.BackupDirectory,
                        errors = result.Errors
                    });
                }
            }
            catch (OperationCanceledException)
            {
                PostInstallEvent("installCancelled", new { runId, operationIndex = -1, backupDirectory = plan.BackupDirectory });
            }
            catch (Exception ex)
            {
                PostInstallEvent("installFailed", new
                {
                    runId,
                    error = ex.Message,
                    stage = "Failed",
                    operationIndex = -1,
                    backupDirectory = plan.BackupDirectory,
                    errors = new[] { ex.ToString() }
                });
            }
            finally
            {
                lock (_installRunLock)
                {
                    if (_activeInstallRun?.RunId == runId)
                    {
                        _activeInstallRun.Cts.Dispose();
                        _activeInstallRun = null;
                    }
                }
            }
        });

        lock (_installRunLock)
        {
            if (_activeInstallRun?.RunId == runId)
            {
                _activeInstallRun = _activeInstallRun with { Task = task };
            }
        }

        return new { runId, plan };
    }

    private object? CancelInstall(JsonElement payload)
    {
        var runId = ReadString(payload, "runId");
        lock (_installRunLock)
        {
            if (_activeInstallRun is not null && (string.IsNullOrEmpty(runId) || _activeInstallRun.RunId == runId))
            {
                try { _activeInstallRun.Cts.Cancel(); } catch { /* swallow */ }
            }
        }
        return null;
    }

    private object RollbackInstall(JsonElement payload)
    {
        lock (_installRunLock)
        {
            if (_activeInstallRun is not null)
            {
                throw new InvalidOperationException("当前有安装任务在运行,请先等其完成或取消再回滚。");
            }
        }

        var backupDirectory = ReadString(payload, "backupDirectory");
        var gameRoot = RequireGameRoot(payload);
        var progress = new Progress<InstallProgress>(p => PostInstallProgress("rollback", p));
        return _installExecutor.RollbackAsync(backupDirectory, gameRoot, progress, CancellationToken.None).GetAwaiter().GetResult();
    }

    private static object GetEmbeddedBundleInfo()
    {
        return EmbeddedAssetCatalog.All.Select(asset => new
        {
            asset.Key,
            Kind = asset.Kind.ToString(),
            Runtime = asset.Runtime.ToString(),
            Backend = asset.Backend.ToString(),
            asset.Version,
            asset.Sha256,
            asset.SizeBytes
        }).ToArray();
    }

    private void PostInstallProgress(string runId, InstallProgress progress)
    {
        PostInstallEvent("installProgress", new
        {
            runId,
            operationIndex = progress.OperationIndex,
            operationCount = progress.OperationCount,
            stage = progress.Stage.ToString(),
            message = progress.Message,
            percent = progress.Percent,
            currentDestination = progress.CurrentDestination
        });
    }

    private void PostInstallEvent(string type, object payload)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => PostInstallEvent(type, payload));
            return;
        }

        var envelope = new Dictionary<string, object?>(StringComparer.Ordinal) { ["type"] = type };
        foreach (var prop in payload.GetType().GetProperties())
        {
            envelope[prop.Name] = prop.GetValue(payload);
        }

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        try
        {
            WebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch
        {
            // WebView2 在关闭过程中可能拒绝消息,忽略以免阻塞后台任务收尾。
        }
    }

    private sealed record InstallRunState(string RunId, CancellationTokenSource Cts, Task? Task);

    private static FilePickResult PickFile(JsonElement payload)
    {
        using var dialog = new WinForms.OpenFileDialog
        {
            Title = ReadString(payload, "title", "选择文件"),
            Filter = ReadString(payload, "filter", "所有文件 (*.*)|*.*"),
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        var initialDirectory = ReadString(payload, "initialDirectory");
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog() != WinForms.DialogResult.OK)
        {
            return FilePickResult.Cancelled();
        }

        return FilePickResult.Selected(dialog.FileName);
    }

    private sealed record FilePickResult(string Status, string? FilePath)
    {
        public static FilePickResult Cancelled() => new("cancelled", null);
        public static FilePickResult Selected(string path) => new("selected", path);
    }

    private static object LoadPluginConfig(JsonElement payload)
    {
        return LoadPluginConfigForGame(RequireGameRoot(payload));
    }

    private static object SavePluginConfig(JsonElement payload)
    {
        var gameRoot = RequireGameRoot(payload);
        var settingsPath = GetPluginSettingsPath(gameRoot);
        var store = new CfgControlPanelSettingsStore(settingsPath);
        var settings = store.Load();
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("config", out var configElement) &&
            configElement.ValueKind == JsonValueKind.Object)
        {
            settings.Config = JsonSerializer.Deserialize<UpdateConfigRequest>(configElement.GetRawText(), JsonOptions) ?? settings.Config;
        }

        store.Save(settings);
        return LoadPluginConfigForGame(gameRoot);
    }

    private static object LoadPluginConfigForGame(string gameRoot)
    {
        var settingsPath = GetPluginSettingsPath(gameRoot);
        var store = new CfgControlPanelSettingsStore(settingsPath);
        var settings = store.Load();
        return new
        {
            SettingsPath = settingsPath,
            settings.Config,
            ProviderDirectory = Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "providers"),
            TextureImageProviderDirectory = Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "texture-image-providers")
        };
    }

    private static TranslationCachePage QueryTranslations(JsonElement payload)
    {
        using var cache = OpenTranslationCache(payload);
        var query = new TranslationCacheQuery(
            ReadString(payload, "search"),
            ReadString(payload, "sort", "updated_utc"),
            string.Equals(ReadString(payload, "direction", "desc"), "desc", StringComparison.OrdinalIgnoreCase),
            ReadInt(payload, "offset", 0),
            Math.Clamp(ReadInt(payload, "limit", 100), 1, 500),
            ReadColumnFilters(payload));
        return cache.Query(query);
    }

    private static TranslationCacheFilterOptionPage GetTranslationFilterOptions(JsonElement payload)
    {
        using var cache = OpenTranslationCache(payload);
        var query = new TranslationCacheFilterOptionsQuery(
            ReadString(payload, "column"),
            ReadString(payload, "search"),
            ReadColumnFilters(payload),
            ReadString(payload, "optionSearch"),
            Math.Clamp(ReadInt(payload, "limit", 80), 1, 200));
        return cache.GetFilterOptions(query);
    }

    private static TranslationCacheEntry UpdateTranslation(JsonElement payload)
    {
        using var cache = OpenTranslationCache(payload);
        var entry = ReadEntry(payload, "entry");
        cache.Update(entry);
        return entry;
    }

    private static object DeleteTranslations(JsonElement payload)
    {
        using var cache = OpenTranslationCache(payload);
        var deleted = 0;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("entries", out var entriesElement) &&
            entriesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entryElement in entriesElement.EnumerateArray())
            {
                var entry = JsonSerializer.Deserialize<TranslationCacheEntry>(entryElement.GetRawText(), JsonOptions);
                if (entry == null)
                {
                    continue;
                }

                cache.Delete(entry);
                deleted++;
            }
        }

        return new { DeletedCount = deleted };
    }

    private static string ExportTranslations(JsonElement payload)
    {
        using var cache = OpenTranslationCache(payload);
        return cache.Export(ReadString(payload, "format", "json"));
    }

    private static TranslationCacheImportResult ImportTranslations(JsonElement payload)
    {
        using var cache = OpenTranslationCache(payload);
        return cache.Import(ReadString(payload, "content"), ReadString(payload, "format", "json"));
    }

    private static DatabaseMaintenanceResult RunDatabaseMaintenance(JsonElement payload)
    {
        var databasePath = ReadString(payload, "databasePath");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            databasePath = ResolveTranslationDatabasePath(payload);
        }

        return TranslationDatabaseService.RunMaintenance(new DatabaseMaintenanceRequest(
            DatabasePath: databasePath,
            CreateBackup: ReadBool(payload, "createBackup", fallback: true),
            RunIntegrityCheck: ReadBool(payload, "runIntegrityCheck", fallback: true),
            Reindex: ReadBool(payload, "reindex"),
            Vacuum: ReadBool(payload, "vacuum")));
    }

    private void PostResponse(ToolboxBridgeResponse response)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);
        WebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private static string RequireGameRoot(JsonElement payload)
    {
        var gameRoot = ReadString(payload, "gameRoot");
        if (string.IsNullOrWhiteSpace(gameRoot))
        {
            throw new InvalidOperationException("请先选择游戏目录。");
        }

        return Path.GetFullPath(gameRoot);
    }

    private static string GetPluginSettingsPath(string gameRoot)
    {
        return Path.Combine(gameRoot, "BepInEx", "config", "com.hanfeng.hunityautotranslator.cfg");
    }

    private static string ResolveTranslationDatabasePath(JsonElement payload)
    {
        var databasePath = ReadString(payload, "databasePath");
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            var gameRoot = RequireGameRoot(payload);
            databasePath = Path.Combine(gameRoot, "BepInEx", "config", "HUnityAutoTranslator", "translation-cache.sqlite");
        }

        databasePath = Path.GetFullPath(databasePath);
        if (!File.Exists(databasePath))
        {
            throw new FileNotFoundException("未找到翻译缓存数据库。", databasePath);
        }

        return databasePath;
    }

    private static SqliteTranslationCache OpenTranslationCache(JsonElement payload)
    {
        return new SqliteTranslationCache(ResolveTranslationDatabasePath(payload));
    }

    private static TranslationCacheEntry ReadEntry(JsonElement payload, string propertyName)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("缺少译文行数据。");
        }

        return JsonSerializer.Deserialize<TranslationCacheEntry>(value.GetRawText(), JsonOptions)
            ?? throw new InvalidOperationException("译文行数据格式无效。");
    }

    private static IReadOnlyList<TranslationCacheColumnFilter> ReadColumnFilters(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("columnFilters", out var filtersElement) ||
            filtersElement.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<TranslationCacheColumnFilter>();
        }

        var filters = new List<TranslationCacheColumnFilter>();
        foreach (var property in filtersElement.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var values = property.Value
                .EnumerateArray()
                .Select(item => item.ValueKind == JsonValueKind.Null ? null : item.GetString())
                .ToArray();
            filters.Add(new TranslationCacheColumnFilter(property.Name, values));
        }

        return filters;
    }

    private static string ReadString(JsonElement payload, string propertyName, string fallback = "")
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return fallback;
        }

        return value.GetString() ?? fallback;
    }

    private static bool ReadBool(JsonElement payload, string propertyName, bool fallback = false)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static int ReadInt(JsonElement payload, string propertyName, int fallback = 0)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty(propertyName, out var value))
        {
            return fallback;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed)
            ? parsed
            : fallback;
    }

    private static TEnum ReadEnum<TEnum>(JsonElement payload, string propertyName, TEnum fallback)
        where TEnum : struct, Enum
    {
        var value = ReadString(payload, propertyName);
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static TEnum? ReadNullableEnum<TEnum>(JsonElement payload, string propertyName)
        where TEnum : struct, Enum
    {
        var value = ReadString(payload, propertyName);
        if (string.IsNullOrWhiteSpace(value)) return null;
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : (TEnum?)null;
    }

    private static string? ReadNullableString(JsonElement payload, string propertyName)
    {
        var value = ReadString(payload, propertyName);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            AdjustMaximizedBounds(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void AdjustMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero)
        {
            return;
        }

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        Marshal.StructureToPtr(mmi, lParam, true);
    }

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private const string BridgeScript = """
(() => {
  if (window.ToolboxBridge) {
    return;
  }

  let nextId = 1;
  const pending = new Map();
  const events = new EventTarget();

  window.ToolboxBridge = {
    events,
    invoke(command, payload = {}) {
      return new Promise((resolve, reject) => {
        const id = String(nextId++);
        pending.set(id, { resolve, reject });
        window.chrome.webview.postMessage({ id, command, payload });
      });
    }
  };

  window.chrome.webview.addEventListener("message", (event) => {
    const data = event.data;
    if (data && typeof data === "object" && data.id) {
      const callbacks = pending.get(data.id);
      if (!callbacks) {
        return;
      }

      pending.delete(data.id);
      if (data.succeeded) {
        callbacks.resolve(data.payload);
      } else {
        callbacks.reject(new Error(data.error || "工具箱命令失败"));
      }
      return;
    }

    if (data && typeof data === "object" && typeof data.type === "string") {
      events.dispatchEvent(new CustomEvent(data.type, { detail: data }));
    }
  });
})();
""";

    private sealed record ToolboxBridgeRequest(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("command")] string Command,
        [property: JsonPropertyName("payload")] JsonElement Payload);

    private sealed record ToolboxBridgeResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("succeeded")] bool Succeeded,
        [property: JsonPropertyName("payload")] object? Payload,
        [property: JsonPropertyName("error")] string? Error);
}
