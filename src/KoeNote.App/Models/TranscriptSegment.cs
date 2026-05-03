namespace KoeNote.App.Models;

public sealed record TranscriptSegment(
    string SegmentId,
    string JobId,
    double StartSeconds,
    double EndSeconds,
    string? SpeakerId,
    string RawText,
    string? NormalizedText = null,
    double? AsrConfidence = null,
    string Source = "asr");
