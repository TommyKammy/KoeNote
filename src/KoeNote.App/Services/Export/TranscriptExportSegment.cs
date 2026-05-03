namespace KoeNote.App.Services.Export;

public sealed record TranscriptExportSegment(
    string SegmentId,
    double StartSeconds,
    double EndSeconds,
    string Speaker,
    string Text);
