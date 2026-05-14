using FluentAssertions;
using HUnityAutoTranslator.Core.Configuration;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class TextureImageTranslationSettingsTests
{
    [Fact]
    public void Snapshot_reports_default_texture_image_translation_settings_without_api_key()
    {
        var service = ControlPanelService.CreateDefault();

        var state = service.GetState();

        state.TextureImageTranslation.Enabled.Should().BeFalse();
        state.TextureImageTranslation.BaseUrl.Should().Be("https://api.openai.com");
        state.TextureImageTranslation.EditEndpoint.Should().Be("/v1/images/edits");
        state.TextureImageTranslation.VisionEndpoint.Should().Be("/v1/responses");
        state.TextureImageTranslation.ImageModel.Should().Be("gpt-image-2");
        state.TextureImageTranslation.VisionModel.Should().Be("gpt-5.4-mini");
        state.TextureImageTranslation.Quality.Should().Be("medium");
        state.TextureImageTranslation.TimeoutSeconds.Should().Be(180);
        state.TextureImageTranslation.MaxConcurrentRequests.Should().Be(1);
        state.TextureImageTranslation.EnableVisionConfirmation.Should().BeTrue();
        state.TextureImageApiKeyConfigured.Should().BeFalse();
        service.GetTextureImageApiKey().Should().BeNull();
    }

    [Fact]
    public void UpdateConfig_clamps_texture_image_translation_settings()
    {
        var service = ControlPanelService.CreateDefault();

        service.UpdateConfig(new UpdateConfigRequest(
            TextureImageTranslation: new TextureImageTranslationConfig(
                Enabled: true,
                BaseUrl: "  https://api.openai.com/  ",
                EditEndpoint: "images/edits",
                VisionEndpoint: "responses",
                ImageModel: "  gpt-image-2  ",
                VisionModel: "  gpt-5.4-mini  ",
                Quality: "ultra",
                TimeoutSeconds: 999,
                MaxConcurrentRequests: 8,
                EnableVisionConfirmation: true)));

        var state = service.GetState();

        state.TextureImageTranslation.Enabled.Should().BeTrue();
        state.TextureImageTranslation.BaseUrl.Should().Be("https://api.openai.com");
        state.TextureImageTranslation.EditEndpoint.Should().Be("/images/edits");
        state.TextureImageTranslation.VisionEndpoint.Should().Be("/responses");
        state.TextureImageTranslation.ImageModel.Should().Be("gpt-image-2");
        state.TextureImageTranslation.VisionModel.Should().Be("gpt-5.4-mini");
        state.TextureImageTranslation.Quality.Should().Be("medium");
        state.TextureImageTranslation.TimeoutSeconds.Should().Be(300);
        state.TextureImageTranslation.MaxConcurrentRequests.Should().Be(4);
    }

    [Fact]
    public void Legacy_texture_image_key_is_not_rewritten_to_cfg_after_profile_migration()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "com.hanfeng.hunityautotranslator.cfg");
        var profileDirectory = Path.Combine(Path.GetDirectoryName(path)!, "texture-image-providers");
        var first = ControlPanelService.CreateDefault(
            new CfgControlPanelSettingsStore(path),
            providerProfileStore: null,
            textureImageProviderProfileStore: new EncryptedTextureImageProviderProfileStore(profileDirectory));

        first.SetTextureImageApiKey("texture-secret");
        first.UpdateConfig(new UpdateConfigRequest(TextureImageTranslation: TextureImageTranslationConfig.Default() with
        {
            Enabled = true,
            BaseUrl = "http://192.168.2.10:8317"
        }));

        var cfg = File.ReadAllText(path);
        cfg.Should().NotContain("texture-secret");
        cfg.Should().NotContain("TextureImageSecret =");

        var second = ControlPanelService.CreateDefault(
            new CfgControlPanelSettingsStore(path),
            providerProfileStore: null,
            textureImageProviderProfileStore: new EncryptedTextureImageProviderProfileStore(profileDirectory));
        second.GetReadyTextureImageProviderProfiles().Should().ContainSingle().Which.ApiKey.Should().Be("texture-secret");
        second.GetState().TextureImageProviderProfiles.Should().ContainSingle().Which.ApiKeyConfigured.Should().BeTrue();
    }

    [Fact]
    public void Texture_image_test_endpoints_use_real_image_edit_client_instead_of_model_listing()
    {
        var serverSource = File.ReadAllText(FindRepositoryFile("src", "HUnityAutoTranslator.Plugin", "Web", "LocalHttpServer.cs"));

        serverSource.Should().Contain("new TextureImageEditClient(_httpTransport");
        serverSource.Should().Contain(".TestConnectionAsync(normalized.ToConfig()");
        serverSource.Should().NotContain("var models = await utilityClient.FetchModelsAsync(utilityProfile, CancellationToken.None)");
        serverSource.Should().NotContain(".FetchModelsAsync(profile, CancellationToken.None)\r\n                .ConfigureAwait(false);\r\n            return new ProviderTestResult(models.Succeeded, models.Message);");
    }

    private static string FindRepositoryFile(params string[] relativeSegments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "HUnityAutoTranslator.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("tests should run from inside the repository checkout");
        return Path.Combine(new[] { directory!.FullName }.Concat(relativeSegments).ToArray());
    }
}
