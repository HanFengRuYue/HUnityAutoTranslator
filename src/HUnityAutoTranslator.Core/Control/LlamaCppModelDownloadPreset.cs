namespace HUnityAutoTranslator.Core.Control;

public sealed record LlamaCppModelDownloadPreset(
    string Id,
    string Label,
    string ModelScopeModelId,
    string FileName,
    long FileSizeBytes,
    string Sha256,
    string Quantization,
    string UseCase,
    string License,
    string Notes)
{
    public string DownloadUrl => $"https://www.modelscope.cn/models/{ModelScopeModelId}/resolve/master/{FileName}";
}
