using System.Text;
using FluentAssertions;
using HUnityAutoTranslator.Toolbox.Core.Installation;

namespace HUnityAutoTranslator.Core.Tests.Toolbox;

public sealed class UnityVersionDetectorTests
{
    [Fact]
    public void Returns_null_when_directory_missing()
    {
        var result = UnityVersionDetector.TryDetect(Path.Combine(Path.GetTempPath(), "definitely-not-a-game-" + Guid.NewGuid()));
        result.Should().BeNull();
    }

    [Fact]
    public void Returns_null_when_data_directory_absent()
    {
        var root = Path.Combine(Path.GetTempPath(), "HUnityVersionDetectorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            UnityVersionDetector.TryDetect(root).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Extracts_unity_version_from_globalgamemanagers_header()
    {
        var root = CreateUnityLikeGame("2022.3.21f1");
        try
        {
            var detected = UnityVersionDetector.TryDetect(root);
            detected.Should().NotBeNull();
            detected!.Version.Should().Be("2022.3.21f1");
            detected.SourceFile.Should().EndWith("globalgamemanagers");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Extracts_version_from_data_unity3d_when_globalgamemanagers_missing()
    {
        var root = CreateUnityLikeGame("2021.3.45f1", useDataUnity3d: true);
        try
        {
            var detected = UnityVersionDetector.TryDetect(root);
            detected.Should().NotBeNull();
            detected!.Version.Should().Be("2021.3.45f1");
            detected.SourceFile.Should().EndWith("data.unity3d");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Falls_back_to_null_when_no_version_string_found()
    {
        var root = Path.Combine(Path.GetTempPath(), "HUnityVersionDetectorTests", Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "Sample_Data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllBytes(Path.Combine(dataDir, "globalgamemanagers"), new byte[256]);  // all zeros, no version
        try
        {
            UnityVersionDetector.TryDetect(root).Should().BeNull();
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    private static string CreateUnityLikeGame(string version, bool useDataUnity3d = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "HUnityVersionDetectorTests", Guid.NewGuid().ToString("N"));
        var dataDir = Path.Combine(root, "Sample_Data");
        Directory.CreateDirectory(dataDir);

        // Approximate Unity SerializedFile header layout: 20 bytes of metadata fields,
        // then a null-terminated ASCII version string. Detector only needs to find the
        // version pattern within the first 4KB, so the binary preamble can be filler.
        var fileName = useDataUnity3d ? "data.unity3d" : "globalgamemanagers";
        using var stream = File.Create(Path.Combine(dataDir, fileName));
        stream.Write(new byte[20]);
        var versionBytes = Encoding.ASCII.GetBytes(version);
        stream.Write(versionBytes);
        stream.WriteByte(0);
        stream.Write(new byte[64]);

        return root;
    }
}
