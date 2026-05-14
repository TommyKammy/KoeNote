using KoeNote.App.Models;

namespace KoeNote.App.Services.Jobs;

// StageProgressPercent updates the visible stage timeline. JobProgressPercent updates the job card.
public sealed record JobRunUpdate(
    JobRunStage? Stage = null,
    JobRunStageState? StageState = null,
    int? StageProgressPercent = null,
    TimeSpan? Duration = null,
    string? ErrorCategory = null,
    string? StageStatusText = null,
    int? JobProgressPercent = null,
    string? LatestLog = null,
    bool RefreshJobViews = false,
    bool RefreshLogs = false,
    IReadOnlyList<TranscriptSegment>? Segments = null,
    IReadOnlyList<CorrectionDraft>? Drafts = null,
    bool ClearReviewPreview = false);
