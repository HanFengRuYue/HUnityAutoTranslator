namespace HUnityAutoTranslator.Plugin.Unity;

internal interface IUnityTextTarget
{
    string Id { get; }

    bool IsAlive { get; }

    bool IsVisible { get; }

    string? GetText();

    void SetText(string value);
}
