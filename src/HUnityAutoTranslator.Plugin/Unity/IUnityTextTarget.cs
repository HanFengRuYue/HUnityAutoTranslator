namespace HUnityAutoTranslator.Plugin.Unity;

internal interface IUnityTextTarget
{
    string Id { get; }

    UnityEngine.Object Component { get; }

    bool IsAlive { get; }

    bool IsVisible { get; }

    string? SceneName { get; }

    string? HierarchyPath { get; }

    string ComponentType { get; }

    string? GetText();

    void SetText(string value);

    bool TryGetFontSize(out float fontSize);

    bool TrySetFontSize(float fontSize);

    bool TryGetScreenRect(out UnityEngine.Rect screenRect);
}
