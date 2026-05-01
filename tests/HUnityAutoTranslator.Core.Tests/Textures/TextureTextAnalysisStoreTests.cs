using FluentAssertions;
using HUnityAutoTranslator.Core.Textures;

namespace HUnityAutoTranslator.Core.Tests.Textures;

public sealed class TextureTextAnalysisStoreTests
{
    [Fact]
    public void Upsert_persists_texture_text_analysis_by_source_hash()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureTextAnalysisStore(Path.Combine(temp.Path, "texture-analysis.json"));
        var updatedUtc = DateTimeOffset.Parse("2026-05-01T08:00:00Z");

        store.Upsert(new TextureTextAnalysis(
            SourceHash: "abc123",
            Status: TextureTextStatus.NeedsManualReview,
            Confidence: 0.62,
            DetectedText: "START",
            Reason: "poster-like high contrast layout",
            NeedsManualReview: true,
            UserReviewed: false,
            UpdatedUtc: updatedUtc,
            LastError: null));

        var reloaded = new TextureTextAnalysisStore(Path.Combine(temp.Path, "texture-analysis.json"));

        reloaded.TryGet("abc123", out var analysis).Should().BeTrue();
        analysis!.Status.Should().Be(TextureTextStatus.NeedsManualReview);
        analysis.Confidence.Should().Be(0.62);
        analysis.DetectedText.Should().Be("START");
        analysis.UpdatedUtc.Should().Be(updatedUtc);
    }

    [Fact]
    public void Load_accepts_legacy_array_index_format()
    {
        using var temp = new TemporaryDirectory();
        var path = Path.Combine(temp.Path, "texture-analysis.json");
        File.WriteAllText(path, """
            [
              {
                "SourceHash": "poster123",
                "Status": 2,
                "Confidence": 0.71,
                "DetectedText": "SALE",
                "Reason": "legacy array",
                "NeedsManualReview": true,
                "UserReviewed": false,
                "UpdatedUtc": "2026-05-01T10:00:00+00:00",
                "LastError": null
              }
            ]
            """);

        var store = new TextureTextAnalysisStore(path);

        store.TryGet("poster123", out var analysis).Should().BeTrue();
        analysis!.Status.Should().Be(TextureTextStatus.Candidate);
        analysis.DetectedText.Should().Be("SALE");
        analysis.Reason.Should().Be("legacy array");
    }

    [Fact]
    public void Mark_updates_user_reviewed_status_without_losing_detected_text()
    {
        using var temp = new TemporaryDirectory();
        var store = new TextureTextAnalysisStore(Path.Combine(temp.Path, "texture-analysis.json"));
        store.Upsert(TextureTextAnalysis.Unknown("abc123") with
        {
            DetectedText = "PLAY",
            Reason = "local candidate"
        });

        var marked = store.Mark("abc123", TextureTextStatus.ConfirmedText, DateTimeOffset.Parse("2026-05-01T09:00:00Z"));

        marked.Status.Should().Be(TextureTextStatus.ConfirmedText);
        marked.UserReviewed.Should().BeTrue();
        marked.DetectedText.Should().Be("PLAY");
        store.TryGet("abc123", out var persisted).Should().BeTrue();
        persisted!.Status.Should().Be(TextureTextStatus.ConfirmedText);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hunity-texture-analysis-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
