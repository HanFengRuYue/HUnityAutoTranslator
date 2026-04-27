using HUnityAutoTranslator.Core.Configuration;

namespace HUnityAutoTranslator.Core.Control;

public sealed record LlamaCppServerStatus(
    string State,
    string Backend,
    string? ModelPath,
    int Port,
    string Message,
    string? LastOutput,
    bool Installed,
    string? Release,
    string? Variant,
    string? ServerPath)
{
    public static LlamaCppServerStatus Stopped(
        LlamaCppConfig config,
        string backend = "",
        bool installed = false,
        string? release = null,
        string? variant = null,
        string? serverPath = null)
    {
        return new LlamaCppServerStatus(
            "stopped",
            backend,
            config.ModelPath,
            0,
            "本地模型未启动。",
            null,
            installed,
            release,
            variant,
            serverPath);
    }

    public static LlamaCppServerStatus Starting(
        LlamaCppConfig config,
        string backend,
        int port,
        string? release = null,
        string? variant = null,
        string? serverPath = null)
    {
        return new LlamaCppServerStatus(
            "starting",
            backend,
            config.ModelPath,
            port,
            "正在启动 llama.cpp 本地模型。",
            null,
            true,
            release,
            variant,
            serverPath);
    }

    public static LlamaCppServerStatus Running(
        LlamaCppConfig config,
        string backend,
        int port,
        string? release = null,
        string? variant = null,
        string? serverPath = null,
        string? lastOutput = null)
    {
        return new LlamaCppServerStatus(
            "running",
            backend,
            config.ModelPath,
            port,
            "llama.cpp 本地模型运行中。",
            lastOutput,
            true,
            release,
            variant,
            serverPath);
    }

    public static LlamaCppServerStatus Error(
        LlamaCppConfig config,
        string backend,
        string message,
        string? lastOutput = null,
        int port = 0,
        bool installed = false,
        string? release = null,
        string? variant = null,
        string? serverPath = null)
    {
        return new LlamaCppServerStatus(
            "error",
            backend,
            config.ModelPath,
            port,
            message,
            lastOutput,
            installed,
            release,
            variant,
            serverPath);
    }
}
