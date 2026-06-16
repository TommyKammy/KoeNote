namespace KoeNote.App.Models;

public sealed record ReadableDocumentBlock(
    string Speaker,
    string TimeRange,
    string Text,
    int SourceLineIndex,
    double? StartSeconds,
    double? EndSeconds)
{
    public bool HasSpeaker => !string.IsNullOrWhiteSpace(Speaker);

    public bool HasTimeRange => !string.IsNullOrWhiteSpace(TimeRange);

    public bool HasMeta => HasSpeaker || HasTimeRange;
}
