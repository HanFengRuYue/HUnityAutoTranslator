namespace HUnityAutoTranslator.Core.Control;

public sealed class ControlPanelSettings
{
    public UpdateConfigRequest Config { get; set; } = new();

    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? ApiKey { get; set; }

    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? EncryptedApiKey { get; set; }

    [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
    public string? TextureImageEncryptedSecret { get; set; }
}
