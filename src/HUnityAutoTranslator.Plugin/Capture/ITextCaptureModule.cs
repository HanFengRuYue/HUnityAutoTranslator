namespace HUnityAutoTranslator.Plugin.Capture;

internal interface ITextCaptureModule : IDisposable
{
    string Name { get; }

    bool IsEnabled { get; }

    void Start();

    void Tick();
}
