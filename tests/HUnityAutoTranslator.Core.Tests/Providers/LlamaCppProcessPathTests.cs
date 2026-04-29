using FluentAssertions;
using HUnityAutoTranslator.Core.Providers;

namespace HUnityAutoTranslator.Core.Tests.Providers;

public sealed class LlamaCppProcessPathTests
{
    [Fact]
    public void NormalizeModelPathForWorkingDirectory_returns_relative_path_for_model_on_same_drive()
    {
        var pluginRoot = Path.Combine(
            "D:\\",
            "Game",
            "池袋セクサロイド女学園",
            "BepInEx",
            "plugins",
            "HUnityAutoTranslator");
        var llamaDirectory = Path.Combine(pluginRoot, "llama.cpp");
        var modelPath = Path.Combine(
            pluginRoot,
            "models",
            "qwen3-1p7b-q8",
            "Qwen3-1.7B-Q8_0.gguf");

        var normalized = LlamaCppProcessPath.NormalizeModelPathForWorkingDirectory(modelPath, llamaDirectory);

        normalized.Should().Be(Path.Combine("..", "models", "qwen3-1p7b-q8", "Qwen3-1.7B-Q8_0.gguf"));
        Path.GetFullPath(normalized, llamaDirectory).Should().Be(modelPath);
    }

    [Fact]
    public void NormalizeModelPathForWorkingDirectory_keeps_relative_model_path_unchanged()
    {
        var modelPath = Path.Combine("..", "models", "qwen.gguf");

        var normalized = LlamaCppProcessPath.NormalizeModelPathForWorkingDirectory(
            modelPath,
            Path.Combine("D:\\", "Game", "Plugin", "llama.cpp"));

        normalized.Should().Be(modelPath);
    }

    [Fact]
    public void NormalizeModelPathForWorkingDirectory_keeps_cross_drive_model_path_unchanged()
    {
        var modelPath = Path.Combine("E:\\", "Models", "qwen.gguf");
        var llamaDirectory = Path.Combine("D:\\", "Game", "Plugin", "llama.cpp");

        var normalized = LlamaCppProcessPath.NormalizeModelPathForWorkingDirectory(modelPath, llamaDirectory);

        normalized.Should().Be(modelPath);
    }
}
