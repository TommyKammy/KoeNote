namespace KoeNote.App.Services.Export;

public sealed record TranscriptExportResult(
    string JobId,
    string OutputDirectory,
    IReadOnlyList<string> FilePaths,
    int SegmentCount,
    int PendingDraftCount,
    bool HasUnresolvedDrafts);
