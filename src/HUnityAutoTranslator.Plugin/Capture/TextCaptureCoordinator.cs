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

    public void Tick(bool forceFullScan = false, bool skipGlobalObjectScanners = false)
    {
        foreach (var module in _modules)
        {
            if (module.IsEnabled && !(skipGlobalObjectScanners && module.UsesGlobalObjectScan))
            {
                module.Tick(forceFullScan);
            }
        }
    }

    public void Dispose()
    {
        foreach (var module in _modules)
        {
            module.Dispose();
        }
    }
}
