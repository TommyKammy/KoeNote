namespace KoeNote.App.Services.Export;

public sealed record TranscriptExportRenderResult(
    string JobId,
    string Content,
    int SegmentCount,
    int PendingDraftCount,
    bool HasUnresolvedDrafts);
