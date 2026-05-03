namespace KoeNote.App.Models;

public sealed record TranscriptSegmentPreview(
    string Start,
    string End,
    string Speaker,
    string Text,
    string ReviewState,
    string SegmentId = "");
