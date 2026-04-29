namespace HUnityAutoTranslator.Core.Control;

public static class LlamaCppModelDownloadPresets
{
    public static IReadOnlyList<LlamaCppModelDownloadPreset> All { get; } = new[]
    {
        new LlamaCppModelDownloadPreset(
            Id: "qwen36-27b-q3km",
            Label: "英翻中质量档 - Qwen3.6 27B Q3_K_M",
            ModelScopeModelId: "unsloth/Qwen3.6-27B-GGUF",
            FileName: "Qwen3.6-27B-Q3_K_M.gguf",
            FileSizeBytes: 13586217184,
            Sha256: "bdfa99d488b501f5ca711f35c930f005319a064eabcaae23290571a25c499eeb",
            Quantization: "Q3_K_M",
            UseCase: "英翻中质量优先，16GB 显存可尝试。",
            License: "apache-2.0",
            Notes: "质量优先预设，比 9B 更慢也更占显存。"),
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
            Notes: "文件接近 16GB，显存紧张时优先选 Q3_K_M 或 9B。"),
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
            Id: "sakura-14b-qwen3-iq4xs",
            Label: "日翻中特化轻量 - Sakura 14B IQ4XS",
            ModelScopeModelId: "sakuraumi/Sakura-14B-Qwen3-v1.5-GGUF",
            FileName: "sakura-14b-qwen3-v1.5-iq4xs.gguf",
            FileSizeBytes: 8180361440,
            Sha256: "cd1a129b19c711fb67f232e7feadc3cf0b2183e9408d47ac222f14123dae5436",
            Quantization: "IQ4XS",
            UseCase: "日翻中轻量档，适合显存余量较小的场景。",
            License: "CC-BY-NC-SA-4.0 / 非商用",
            Notes: "非商用许可证；比 Q6K 更省显存。"),
        new LlamaCppModelDownloadPreset(
            Id: "qwen3-14b-q4km",
            Label: "兼容备选 - Qwen3 14B Q4_K_M",
            ModelScopeModelId: "Qwen/Qwen3-14B-GGUF",
            FileName: "Qwen3-14B-Q4_K_M.gguf",
            FileSizeBytes: 9001752960,
            Sha256: "500a8806e85ee9c83f3ae08420295592451379b4f8cf2d0f41c15dffeb6b81f0",
            Quantization: "Q4_K_M",
            UseCase: "英翻中和通用中文翻译兼容备选。",
            License: "apache-2.0",
            Notes: "旧一些但兼容性较好，文件体积适中。")
    };
}
