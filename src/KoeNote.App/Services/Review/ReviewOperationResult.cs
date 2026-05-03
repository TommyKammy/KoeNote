namespace KoeNote.App.Services.Review;

public sealed record ReviewOperationResult(
    string JobId,
    string SegmentId,
    string DraftId,
    string Action,
    string? FinalText,
    int PendingDraftCount);
