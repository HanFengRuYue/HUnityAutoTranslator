using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Control;

public sealed class JsonControlPanelSettingsStore : IControlPanelSettingsStore
{
    private readonly string _filePath;

    public JsonControlPanelSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public ControlPanelSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new ControlPanelSettings();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<ControlPanelSettings>(json) ?? new ControlPanelSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new ControlPanelSettings();
        }
    }

    public void Save(ControlPanelSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_filePath, JsonConvert.SerializeObject(settings, Formatting.Indented));
    }
}
