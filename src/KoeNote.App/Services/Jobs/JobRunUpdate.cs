using KoeNote.App.Models;

namespace KoeNote.App.Services.Jobs;

public sealed record JobRunUpdate(
    JobRunStage? Stage = null,
    JobRunStageState? StageState = null,
    int? ProgressPercent = null,
    TimeSpan? Duration = null,
    string? ErrorCategory = null,
    string? LatestLog = null,
    bool RefreshJobViews = false,
    bool RefreshLogs = false,
    IReadOnlyList<TranscriptSegment>? Segments = null,
    IReadOnlyList<CorrectionDraft>? Drafts = null,
    bool ClearReviewPreview = false);
