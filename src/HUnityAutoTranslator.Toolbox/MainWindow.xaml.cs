using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
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
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userDataFolder = GetWebViewUserDataFolder();
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder).ConfigureAwait(true);
            await WebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);
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

            var result = await HandleCommandAsync(request).ConfigureAwait(true);
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

    private Task<object?> HandleCommandAsync(ToolboxBridgeRequest request)
    {
        if (TryHandleWindowCommand(request.Command, out var windowResult))
        {
            return Task.FromResult(windowResult);
        }

        return Task.FromResult(HandleCommand(request));
    }

    private bool TryHandleWindowCommand(string command, out object? result)
    {
        result = null;
        switch (command)
        {
            case "windowMinimize":
                WindowState = WindowState.Minimized;
                return true;
            case "windowToggleMaximize":
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return true;
            case "windowClose":
                Close();
                return true;
            case "windowDrag":
                TryDragWindow();
                return true;
            default:
                return false;
        }
    }

    private void TryDragWindow()
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // WebView2 can deliver the bridge call just after the mouse state changes.
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
            "pickFontFile" => PickFontFile(),
            "createInstallPlan" => CreateInstallPlan(payload),
            "loadPluginConfig" => LoadPluginConfig(payload),
            "savePluginConfig" => SavePluginConfig(payload),
            "queryTranslations" => QueryTranslations(payload),
            "getTranslationFilterOptions" => GetTranslationFilterOptions(payload),
            "updateTranslation" => UpdateTranslation(payload),
            "deleteTranslations" => DeleteTranslations(payload),
            "exportTranslations" => ExportTranslations(payload),
            "importTranslations" => ImportTranslations(payload),
            "runDatabaseMaintenance" => RunDatabaseMaintenance(payload),
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
        var options = new InstallPlanOptions(
            PackageVersion: ReadString(payload, "packageVersion", "0.1.1"),
            Mode: ReadEnum(payload, "mode", InstallMode.Full),
            IncludeLlamaCppBackend: ReadBool(payload, "includeLlamaCppBackend"),
            LlamaCppBackend: ReadEnum(payload, "llamaCppBackend", LlamaCppBackendKind.None));

        return InstallPlanner.CreatePlan(inspection, options);
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

    private const string BridgeScript = """
(() => {
  if (window.ToolboxBridge) {
    return;
  }

  let nextId = 1;
  const pending = new Map();

  window.ToolboxBridge = {
    invoke(command, payload = {}) {
      return new Promise((resolve, reject) => {
        const id = String(nextId++);
        pending.set(id, { resolve, reject });
        window.chrome.webview.postMessage({ id, command, payload });
      });
    }
  };

  window.chrome.webview.addEventListener("message", (event) => {
    const response = event.data;
    const callbacks = pending.get(response.id);
    if (!callbacks) {
      return;
    }

    pending.delete(response.id);
    if (response.succeeded) {
      callbacks.resolve(response.payload);
    } else {
      callbacks.reject(new Error(response.error || "工具箱命令失败"));
    }
  });
})();
""";

    private sealed record ToolboxBridgeRequest(string Id, string Command, JsonElement Payload);

    private sealed record ToolboxBridgeResponse(string Id, bool Succeeded, object? Payload, string? Error);
}
