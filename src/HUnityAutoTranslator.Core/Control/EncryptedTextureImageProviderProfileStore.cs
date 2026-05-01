using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Control;

public sealed class EncryptedTextureImageProviderProfileStore : ITextureImageProviderProfileStore
{
    private const string Extension = ".huttextureimage";
    private readonly string _directory;

    public EncryptedTextureImageProviderProfileStore(string directory)
    {
        _directory = directory;
    }

    public IReadOnlyList<TextureImageProviderProfileDefinition> LoadAll()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<TextureImageProviderProfileDefinition>();
        }

        var profiles = new List<TextureImageProviderProfileDefinition>();
        foreach (var file in Directory.GetFiles(_directory, "*" + Extension))
        {
            try
            {
                var json = PortableTextureImageProviderProfileProtector.Unprotect(File.ReadAllText(file));
                var profile = JsonConvert.DeserializeObject<TextureImageProviderProfileDefinition>(json)?.Normalize();
                if (profile != null)
                {
                    profiles.Add(profile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or System.Security.Cryptography.CryptographicException)
            {
            }
        }

        return profiles
            .OrderBy(profile => profile.Priority)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Save(TextureImageProviderProfileDefinition profile)
    {
        Directory.CreateDirectory(_directory);
        var normalized = profile.Normalize();
        var json = JsonConvert.SerializeObject(normalized, Formatting.None);
        File.WriteAllText(GetPath(normalized.Id), PortableTextureImageProviderProfileProtector.Protect(json));
    }

    public bool Delete(string id)
    {
        var path = GetPath(TextureImageProviderProfileDefinition.NormalizeId(id));
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public string Export(string id)
    {
        return File.ReadAllText(GetPath(TextureImageProviderProfileDefinition.NormalizeId(id)));
    }

    public TextureImageProviderProfileDefinition Import(string content, IReadOnlyCollection<string> existingIds)
    {
        var json = PortableTextureImageProviderProfileProtector.Unprotect(content);
        var profile = JsonConvert.DeserializeObject<TextureImageProviderProfileDefinition>(json)?.Normalize()
            ?? throw new InvalidOperationException("Texture image provider profile import content is empty.");
        if (existingIds.Contains(profile.Id))
        {
            profile = profile with { Id = TextureImageProviderProfileDefinition.CreateId() };
        }

        Save(profile);
        return profile;
    }

    private string GetPath(string id)
    {
        return Path.Combine(_directory, TextureImageProviderProfileDefinition.NormalizeId(id) + Extension);
    }
}
