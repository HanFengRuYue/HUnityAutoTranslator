using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class ProviderProfileFileStoreTests
{
    [Fact]
    public void Save_encrypts_profile_file_and_reloads_from_another_store_instance()
    {
        var directory = NewDirectory();
        var store = new EncryptedProviderProfileStore(directory);
        var profile = ProviderProfileDefinition.CreateDefault("OpenAI 主配置", ProviderKind.OpenAI, priority: 10) with
        {
            BaseUrl = "https://api.example.test",
            Endpoint = "/v1/responses",
            Model = "gpt-test",
            ApiKey = "secret-profile-key",
            MaxConcurrentRequests = 9,
            RequestsPerMinute = 88,
            RequestTimeoutSeconds = 44
        };

        store.Save(profile);

        var file = Directory.GetFiles(directory, "*.hutprovider").Should().ContainSingle().Which;
        var raw = File.ReadAllText(file);
        raw.Should().StartWith("hutprovider:v1:");
        raw.Should().NotContain("https://api.example.test");
        raw.Should().NotContain("secret-profile-key");

        var reloaded = new EncryptedProviderProfileStore(directory).LoadAll();
        reloaded.Should().ContainSingle();
        reloaded[0].BaseUrl.Should().Be("https://api.example.test");
        reloaded[0].ApiKey.Should().Be("secret-profile-key");
        reloaded[0].MaxConcurrentRequests.Should().Be(9);
        reloaded[0].RequestsPerMinute.Should().Be(88);
        reloaded[0].RequestTimeoutSeconds.Should().Be(44);
    }

    [Fact]
    public void Save_round_trips_a_preset_id()
    {
        var directory = NewDirectory();
        var store = new EncryptedProviderProfileStore(directory);
        var profile = ProviderProfileDefinition.CreateDefault("硅基流动", ProviderKind.OpenAICompatible, priority: 0)
            with { PresetId = "siliconflow" };

        store.Save(profile);

        var reloaded = new EncryptedProviderProfileStore(directory).LoadAll();
        reloaded.Should().ContainSingle();
        reloaded[0].PresetId.Should().Be("siliconflow");
    }

    [Fact]
    public void LoadAll_skips_corrupt_profile_files_without_throwing()
    {
        var directory = NewDirectory();
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "broken.hutprovider"), "not an encrypted provider");

        var act = () => new EncryptedProviderProfileStore(directory).LoadAll();

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    private static string NewDirectory()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "providers");
    }
}
