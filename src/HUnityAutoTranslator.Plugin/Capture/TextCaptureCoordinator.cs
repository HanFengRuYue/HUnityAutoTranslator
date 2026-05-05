namespace HUnityAutoTranslator.Plugin.Capture;

internal sealed class TextCaptureCoordinator : IDisposable
{
    private readonly IReadOnlyList<ITextCaptureModule> _modules;

    public TextCaptureCoordinator(IEnumerable<ITextCaptureModule> modules)
    {
        _modules = modules.ToArray();
    }

    public void Start()
    {
        foreach (var module in _modules)
        {
            module.Start();
        }
    }

    public int Tick(
        bool forceFullScan = false,
        bool skipGlobalObjectScanners = false,
        int? maxGlobalObjectScanTargets = null)
    {
        var processed = 0;
        foreach (var module in _modules)
        {
            if (module.IsEnabled && !(skipGlobalObjectScanners && module.UsesGlobalObjectScan))
            {
                processed += module.Tick(
                    forceFullScan,
                    module.UsesGlobalObjectScan ? maxGlobalObjectScanTargets : null);
            }
        }

        return processed;
    }

    public void Dispose()
    {
        foreach (var module in _modules)
        {
            module.Dispose();
        }
    }
}
