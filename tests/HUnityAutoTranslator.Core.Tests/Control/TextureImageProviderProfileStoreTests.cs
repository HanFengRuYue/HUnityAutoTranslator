using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class TextureImageProviderProfileStoreTests
{
    [Fact]
    public void Save_encrypts_texture_image_profile_file_and_reloads_from_another_store_instance()
    {
        var directory = NewDirectory();
        var store = new EncryptedTextureImageProviderProfileStore(directory);
        var profile = TextureImageProviderProfileDefinition.CreateDefault("贴图主配置", priority: 3) with
        {
            BaseUrl = "https://images.example.test",
            EditEndpoint = "/v1/images/edits",
            VisionEndpoint = "/v1/responses",
            ImageModel = "gpt-image-test",
            VisionModel = "gpt-vision-test",
            ApiKey = "texture-image-secret",
            MaxConcurrentRequests = 3,
            TimeoutSeconds = 222,
            EnableVisionConfirmation = false
        };

        store.Save(profile);

        var file = Directory.GetFiles(directory, "*.huttextureimage").Should().ContainSingle().Which;
        var raw = File.ReadAllText(file);
        raw.Should().StartWith("huttextureimage:v1:");
        raw.Should().NotContain("https://images.example.test");
        raw.Should().NotContain("texture-image-secret");

        var reloaded = new EncryptedTextureImageProviderProfileStore(directory).LoadAll();
        reloaded.Should().ContainSingle();
        reloaded[0].Name.Should().Be("贴图主配置");
        reloaded[0].BaseUrl.Should().Be("https://images.example.test");
        reloaded[0].ApiKey.Should().Be("texture-image-secret");
        reloaded[0].MaxConcurrentRequests.Should().Be(3);
        reloaded[0].TimeoutSeconds.Should().Be(222);
        reloaded[0].EnableVisionConfirmation.Should().BeFalse();
    }

    [Fact]
    public void Control_panel_migrates_legacy_texture_image_cfg_into_encrypted_profile_once()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var profileDirectory = Path.Combine(Path.GetDirectoryName(settingsPath)!, "texture-image-providers");
        var first = ControlPanelService.CreateDefault(
            new CfgControlPanelSettingsStore(settingsPath),
            providerProfileStore: null,
            textureImageProviderProfileStore: new EncryptedTextureImageProviderProfileStore(profileDirectory));

        first.SetTextureImageApiKey("legacy-texture-secret");
        first.UpdateConfig(new UpdateConfigRequest(TextureImageTranslation: TextureImageTranslationConfig.Default() with
        {
            Enabled = true,
            BaseUrl = "https://legacy-images.example.test",
            ImageModel = "legacy-image-model"
        }));

        var second = ControlPanelService.CreateDefault(
            new CfgControlPanelSettingsStore(settingsPath),
            providerProfileStore: null,
            textureImageProviderProfileStore: new EncryptedTextureImageProviderProfileStore(profileDirectory));

        var profiles = second.GetState().TextureImageProviderProfiles;
        profiles.Should().ContainSingle();
        profiles![0].BaseUrl.Should().Be("https://legacy-images.example.test");
        profiles[0].ImageModel.Should().Be("legacy-image-model");
        profiles[0].ApiKeyConfigured.Should().BeTrue();
        second.GetReadyTextureImageProviderProfiles().Should().ContainSingle().Which.ApiKey.Should().Be("legacy-texture-secret");
        File.ReadAllText(settingsPath).Should().NotContain("TextureImageSecret =");
    }

    private static string NewDirectory()
    {
        return Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "texture-image-providers");
    }
}
