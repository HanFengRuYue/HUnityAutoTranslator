namespace HUnityAutoTranslator.Plugin.Capture;

internal interface ITextCaptureModule : IDisposable
{
    string Name { get; }

    bool IsEnabled { get; }

    bool UsesGlobalObjectScan { get; }

    void Start();

    void Tick(bool forceFullScan = false);
}
