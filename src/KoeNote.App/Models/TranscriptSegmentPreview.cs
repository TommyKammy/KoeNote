namespace KoeNote.App.Models;

public sealed record TranscriptSegmentPreview(
    string Start,
    string End,
    string Speaker,
    string Text,
    string ReviewState,
    string SegmentId = "",
    string SpeakerId = "",
    string RawText = "",
    string? NormalizedText = null,
    string? FinalText = null,
    double StartSeconds = 0,
    double EndSeconds = 0);
