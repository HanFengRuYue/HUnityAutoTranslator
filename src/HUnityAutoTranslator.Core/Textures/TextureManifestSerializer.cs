using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Textures;

public static class TextureManifestSerializer
{
    private static readonly JsonSerializerSettings Settings = new()
    {
        NullValueHandling = NullValueHandling.Ignore
    };

    public static string Serialize(TextureExportManifest manifest)
    {
        return JsonConvert.SerializeObject(manifest, Formatting.None, Settings);
    }

    public static TextureExportManifest? Deserialize(string json)
    {
        return JsonConvert.DeserializeObject<TextureExportManifest>(json, Settings);
    }
}
