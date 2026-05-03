namespace KoeNote.App.Models;

public sealed record CorrectionDraft(
    string DraftId,
    string JobId,
    string SegmentId,
    string IssueType,
    string OriginalText,
    string SuggestedText,
    string Reason,
    double Confidence,
    string Status = "pending",
    DateTimeOffset? CreatedAt = null,
    string Source = "llm",
    string? SourceRefId = null);
