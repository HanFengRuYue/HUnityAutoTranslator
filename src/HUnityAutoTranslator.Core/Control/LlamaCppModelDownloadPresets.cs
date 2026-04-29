namespace HUnityAutoTranslator.Core.Control;

public static class LlamaCppModelDownloadPresets
{
    public static IReadOnlyList<LlamaCppModelDownloadPreset> All { get; } = new[]
    {
        new LlamaCppModelDownloadPreset(
            Id: "qwen36-27b-iq4xs",
            Label: "英翻中质量档 - Qwen3.6 27B IQ4_XS",
            ModelScopeModelId: "unsloth/Qwen3.6-27B-GGUF",
            FileName: "Qwen3.6-27B-IQ4_XS.gguf",
            FileSizeBytes: 15440005344,
            Sha256: "8a3365759dc1b33b52c4e7d91d5a67d5ee1418e8408aa54196f04a98da53e5dc",
            Quantization: "IQ4_XS",
            UseCase: "英翻中质量优先，16GB 显存贴边。",
            License: "apache-2.0",
            Notes: "文件接近 16GB，显存紧张时优先选 9B 或更小预设。"),
        new LlamaCppModelDownloadPreset(
            Id: "sakura-14b-qwen3-q6k",
            Label: "日翻中特化 - Sakura 14B Q6K",
            ModelScopeModelId: "sakuraumi/Sakura-14B-Qwen3-v1.5-GGUF",
            FileName: "sakura-14b-qwen3-v1.5-q6k.gguf",
            FileSizeBytes: 12121937120,
            Sha256: "c1314497e632990952826d3703e6832e24425c1314c0bb6d2dc47ef1db37987f",
            Quantization: "Q6K",
            UseCase: "日翻中，适合日文剧情、轻小说和 Galgame 风格文本。",
            License: "CC-BY-NC-SA-4.0 / 非商用",
            Notes: "非商用许可证；商业用途请不要使用。"),
        new LlamaCppModelDownloadPreset(
            Id: "qwen35-9b-q8",
            Label: "英翻中轻量稳定 - Qwen3.5 9B Q8_0",
            ModelScopeModelId: "unsloth/Qwen3.5-9B-GGUF",
            FileName: "Qwen3.5-9B-Q8_0.gguf",
            FileSizeBytes: 9527502048,
            Sha256: "809626574d0cb43d4becfa56169980da2bb448f2299270f7be443cb89d0a6ae4",
            Quantization: "Q8_0",
            UseCase: "英翻中轻量稳定，速度和显存余量更好。",
            License: "apache-2.0",
            Notes: "推荐作为 16GB 显存的稳妥默认本地模型。"),
        new LlamaCppModelDownloadPreset(
            Id: "qwen3-4b-q8",
            Label: "英翻中 4B 高质量 - Qwen3 4B Q8_0",
            ModelScopeModelId: "Qwen/Qwen3-4B-GGUF",
            FileName: "Qwen3-4B-Q8_0.gguf",
            FileSizeBytes: 4280404704,
            Sha256: "8c2f07f26af9747e41988551106f149b03eb9b5cb6df636027b6bf6278473300",
            Quantization: "Q8_0",
            UseCase: "英翻中 4B 高质量档，适合希望比 1.7B 更稳但不想下载 9B 的场景。",
            License: "apache-2.0",
            Notes: "4B 中质量优先，文件约 3.99 GiB。"),
        new LlamaCppModelDownloadPreset(
            Id: "qwen3-1p7b-q8",
            Label: "英翻中轻量 - Qwen3 1.7B Q8_0",
            ModelScopeModelId: "Qwen/Qwen3-1.7B-GGUF",
            FileName: "Qwen3-1.7B-Q8_0.gguf",
            FileSizeBytes: 1834426016,
            Sha256: "061b54daade076b5d3362dac252678d17da8c68f07560be70818cace6590cb1a",
            Quantization: "Q8_0",
            UseCase: "英翻中轻量档，适合低显存、快速试用和小规模文本。",
            License: "apache-2.0",
            Notes: "小模型中优先选择新一代 Qwen3。"),
        new LlamaCppModelDownloadPreset(
            Id: "qwen3-0p6b-q8",
            Label: "英翻中超轻量 - Qwen3 0.6B Q8_0",
            ModelScopeModelId: "Qwen/Qwen3-0.6B-GGUF",
            FileName: "Qwen3-0.6B-Q8_0.gguf",
            FileSizeBytes: 639446688,
            Sha256: "9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031",
            Quantization: "Q8_0",
            UseCase: "英翻中超轻量档，适合非常小的显存预算和快速验证。",
            License: "apache-2.0",
            Notes: "体积最小，翻译质量低于更大模型。"),
    };
}
