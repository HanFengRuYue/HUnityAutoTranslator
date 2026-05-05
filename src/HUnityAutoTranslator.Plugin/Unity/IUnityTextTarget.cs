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

    bool TryGetLineSpacing(out float lineSpacing);

    bool TrySetLineSpacing(float lineSpacing);

    bool TryGetFontLineHeight(out float lineHeight);

    bool TryGetPreferredHeight(out float preferredHeight);

    bool TryGetRenderedHeight(out float renderedHeight);

    bool TryGetRectHeight(out float rectHeight);

    bool TryGetScreenRect(out UnityEngine.Rect screenRect);
}
