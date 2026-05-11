namespace KoeNote.App.Services.Export;

internal sealed record TranscriptExportSnapshot(
    string JobId,
    string Title,
    int PendingDraftCount,
    IReadOnlyList<TranscriptExportSegment> Segments,
    string? DocumentContent = null);
