using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

public sealed class UnityBaseLibraryProviderTests
{
    [Fact]
    public async Task Uses_custom_zip_path_without_touching_network_or_cache()
    {
        var sandbox = CreateSandbox(out var gameRoot, out var cacheDir);
        try
        {
            var customZip = Path.Combine(sandbox, "user-provided.zip");
            File.WriteAllText(customZip, "FAKE-UNITY-LIBS");

            var provider = new UnityBaseLibraryProvider(
                cacheDirectory: cacheDir,
                httpDownloaderOverride: (_, _) => throw new InvalidOperationException("network must not be touched in custom zip mode"));

            var result = await provider.EnsureAsync(gameRoot, "2022.3.21f1", customZip, progress: null, CancellationToken.None);

            result.SourceKind.Should().Be("LocalFile");
            File.Exists(result.DestinationPath).Should().BeTrue();
            File.ReadAllText(result.DestinationPath).Should().Be("FAKE-UNITY-LIBS");
        }
        finally
        {
            Directory.Delete(sandbox, true);
        }
    }

    [Fact]
    public async Task Reuses_global_cache_when_zip_already_present_for_version()
    {
        var sandbox = CreateSandbox(out var gameRoot, out var cacheDir);
        try
        {
            // Pre-populate the cache as if we had downloaded this version on a previous run.
            var cachedZip = Path.Combine(cacheDir, "2022.3.21f1.zip");
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(cachedZip, "CACHED-BLOB");

            var provider = new UnityBaseLibraryProvider(
                cacheDirectory: cacheDir,
                httpDownloaderOverride: (_, _) => throw new InvalidOperationException("network must not be touched on cache hit"));

            var result = await provider.EnsureAsync(gameRoot, "2022.3.21f1", customZipPath: null, progress: null, CancellationToken.None);

            result.SourceKind.Should().Be("Cache");
            File.ReadAllText(result.DestinationPath).Should().Be("CACHED-BLOB");
            // Cache copy should still be there for the next game.
            File.Exists(cachedZip).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(sandbox, true);
        }
    }

    [Fact]
    public async Task Downloads_when_cache_empty_then_populates_cache_and_destination()
    {
        var sandbox = CreateSandbox(out var gameRoot, out var cacheDir);
        try
        {
            byte[] downloaded = Encoding.UTF8.GetBytes("DOWNLOADED-PAYLOAD");
            var provider = new UnityBaseLibraryProvider(
                cacheDirectory: cacheDir,
                sourceTemplate: "fake://libs/{VERSION}.zip",
                httpDownloaderOverride: (uri, _) =>
                {
                    uri.Should().Be("fake://libs/2020.3.5f1.zip");
                    return Task.FromResult<Stream>(new MemoryStream(downloaded));
                });

            var result = await provider.EnsureAsync(gameRoot, "2020.3.5f1", customZipPath: null, progress: null, CancellationToken.None);

            result.SourceKind.Should().Be("Download");
            File.ReadAllText(result.DestinationPath).Should().Be("DOWNLOADED-PAYLOAD");
            File.Exists(Path.Combine(cacheDir, "2020.3.5f1.zip")).Should().BeTrue("download should populate the global cache");
        }
        finally
        {
            Directory.Delete(sandbox, true);
        }
    }

    [Fact]
    public async Task Reports_progress_during_download()
    {
        var sandbox = CreateSandbox(out var gameRoot, out var cacheDir);
        try
        {
            byte[] downloaded = new byte[200_000];  // big enough to span multiple buffer reads
            var provider = new UnityBaseLibraryProvider(
                cacheDirectory: cacheDir,
                sourceTemplate: "fake://libs/{VERSION}.zip",
                httpDownloaderOverride: (_, _) => Task.FromResult<Stream>(new MemoryStream(downloaded)));

            var ticks = new List<UnityBaseLibraryProgress>();
            var progress = new Progress<UnityBaseLibraryProgress>(p => ticks.Add(p));

            await provider.EnsureAsync(gameRoot, "2019.4.40f1", customZipPath: null, progress, CancellationToken.None);

            ticks.Should().NotBeEmpty();
            ticks.Should().Contain(p => p.Stage == "Download");
        }
        finally
        {
            Directory.Delete(sandbox, true);
        }
    }

    [Fact]
    public async Task Throws_clear_error_when_custom_zip_path_missing()
    {
        var sandbox = CreateSandbox(out var gameRoot, out var cacheDir);
        try
        {
            var provider = new UnityBaseLibraryProvider(cacheDirectory: cacheDir);
            Func<Task> act = () => provider.EnsureAsync(gameRoot, "2022.3.21f1", Path.Combine(sandbox, "missing.zip"), null, CancellationToken.None);
            await act.Should().ThrowAsync<FileNotFoundException>().WithMessage("*自定义 Unity 库 zip*");
        }
        finally
        {
            Directory.Delete(sandbox, true);
        }
    }

    [Fact]
    public void Target_zip_path_is_inside_BepInEx_unity_libs()
    {
        var provider = new UnityBaseLibraryProvider(cacheDirectory: Path.GetTempPath());
        var target = provider.GetTargetZipPath(@"D:\Games\Sample", "2022.3.21f1");
        target.Replace('\\', '/').Should().EndWith("BepInEx/unity-libs/2022.3.21f1.zip");
    }

    private static string CreateSandbox(out string gameRoot, out string cacheDir)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "HUnityBaseLibTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        gameRoot = Path.Combine(sandbox, "Game");
        Directory.CreateDirectory(gameRoot);
        cacheDir = Path.Combine(sandbox, "cache");
        return sandbox;
    }
}
