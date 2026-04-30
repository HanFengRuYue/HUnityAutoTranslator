using HUnityAutoTranslator.Core.Configuration;
using Newtonsoft.Json;

namespace HUnityAutoTranslator.Core.Control;

public sealed class EncryptedProviderProfileStore : IProviderProfileStore
{
    private const string Extension = ".hutprovider";
    private readonly string _directory;

    public EncryptedProviderProfileStore(string directory)
    {
        _directory = directory;
    }

    public IReadOnlyList<ProviderProfileDefinition> LoadAll()
    {
        if (!Directory.Exists(_directory))
        {
            return Array.Empty<ProviderProfileDefinition>();
        }

        var profiles = new List<ProviderProfileDefinition>();
        foreach (var file in Directory.GetFiles(_directory, "*" + Extension))
        {
            try
            {
                var json = PortableProviderProfileProtector.Unprotect(File.ReadAllText(file));
                var profile = JsonConvert.DeserializeObject<ProviderProfileDefinition>(json)?.Normalize();
                if (profile != null && ProviderProfileDefinition.IsSupportedProfileKind(profile.Kind))
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

    public void Save(ProviderProfileDefinition profile)
    {
        Directory.CreateDirectory(_directory);
        var normalized = profile.Normalize();
        var json = JsonConvert.SerializeObject(normalized, Formatting.None);
        File.WriteAllText(GetPath(normalized.Id), PortableProviderProfileProtector.Protect(json));
    }

    public bool Delete(string id)
    {
        var path = GetPath(ProviderProfileDefinition.NormalizeId(id));
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public string Export(string id)
    {
        return File.ReadAllText(GetPath(ProviderProfileDefinition.NormalizeId(id)));
    }

    public ProviderProfileDefinition Import(string content, IReadOnlyCollection<string> existingIds)
    {
        var json = PortableProviderProfileProtector.Unprotect(content);
        var profile = JsonConvert.DeserializeObject<ProviderProfileDefinition>(json)?.Normalize()
            ?? throw new InvalidOperationException("Provider profile import content is empty.");
        if (existingIds.Contains(profile.Id))
        {
            profile = profile with { Id = ProviderProfileDefinition.CreateId() };
        }

        Save(profile);
        return profile;
    }

    private string GetPath(string id)
    {
        return Path.Combine(_directory, ProviderProfileDefinition.NormalizeId(id) + Extension);
    }
}
