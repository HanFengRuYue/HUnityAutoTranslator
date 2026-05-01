using FluentAssertions;
using HUnityAutoTranslator.Core.Control;

namespace HUnityAutoTranslator.Core.Tests.Control;

public sealed class FontPickResultTests
{
    [Fact]
    public void CopyToDirectory_copies_selected_font_to_config_fonts_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDirectory = Path.Combine(root, "source");
        var targetDirectory = Path.Combine(root, "config-fonts");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "Noto Sans SC.ttf");
        File.WriteAllBytes(sourcePath, new byte[] { 0, 1, 2, 3 });

        var result = FontPickResult.Selected(sourcePath, "Noto Sans SC").CopyToDirectory(targetDirectory);

        result.Status.Should().Be("selected");
        result.FontName.Should().Be("Noto Sans SC");
        result.FilePath.Should().NotBeNull();
        Path.GetDirectoryName(result.FilePath!).Should().Be(targetDirectory);
        Path.GetExtension(result.FilePath!).Should().Be(".ttf");
        File.Exists(result.FilePath!).Should().BeTrue();
        File.ReadAllBytes(result.FilePath!).Should().Equal(new byte[] { 0, 1, 2, 3 });
    }

    [Fact]
    public void CopyToDirectory_rejects_non_font_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "not-a-font.txt");
        File.WriteAllText(sourcePath, "text");

        var result = FontPickResult.Selected(sourcePath, "Not Font").CopyToDirectory(Path.Combine(root, "fonts"));

        result.Status.Should().Be("error");
        result.FilePath.Should().BeNull();
        Directory.Exists(Path.Combine(root, "fonts")).Should().BeFalse();
    }
}
