namespace KoeNote.App.Services.Transcript;

public sealed record TranscriptReadModel(
    string SegmentId,
    double StartSeconds,
    double EndSeconds,
    string Speaker,
    string Text,
    string ReviewState,
    string SpeakerId,
    string RawText,
    string? NormalizedText,
    string? FinalText);
