namespace HUnityAutoTranslator.Core.Pipeline;

public sealed class CapturedText
{
    public CapturedText(string targetId, string sourceText, bool isVisible)
    {
        TargetId = targetId;
        SourceText = sourceText;
        IsVisible = isVisible;
    }

    public string TargetId { get; }

    public string SourceText { get; }

    public bool IsVisible { get; }
}
