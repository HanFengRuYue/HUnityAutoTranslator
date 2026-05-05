using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using HUnityAutoTranslator.Core.Control;
using HUnityAutoTranslator.Toolbox.Core.Database;
using HUnityAutoTranslator.Toolbox.Core.Installation;
using Microsoft.Web.WebView2.Core;

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
            await WebView.EnsureCoreWebView2Async().ConfigureAwait(true);
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

    private static Task<object?> HandleCommandAsync(ToolboxBridgeRequest request)
    {
        return Task.FromResult(HandleCommand(request));
    }

    private static object? HandleCommand(ToolboxBridgeRequest request)
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
            "createInstallPlan" => CreateInstallPlan(payload),
            "loadPluginConfig" => LoadPluginConfig(payload),
            "runDatabaseMaintenance" => RunDatabaseMaintenance(payload),
            _ => throw new InvalidOperationException("未知工具箱命令：" + request.Command)
        };
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
        var gameRoot = ReadString(payload, "gameRoot");
        var settingsPath = Path.Combine(gameRoot, "BepInEx", "config", "com.hanfeng.hunityautotranslator.cfg");
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

    private static DatabaseMaintenanceResult RunDatabaseMaintenance(JsonElement payload)
    {
        return TranslationDatabaseService.RunMaintenance(new DatabaseMaintenanceRequest(
            DatabasePath: ReadString(payload, "databasePath"),
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
