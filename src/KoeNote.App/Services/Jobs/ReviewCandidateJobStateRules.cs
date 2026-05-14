namespace KoeNote.App.Services.Jobs;

internal static class ReviewCandidateJobStateRules
{
    public static readonly JobStateTransition Ready = new("整文待ち", "review_ready", JobRunProgressPlan.ReviewSucceeded);
    public static readonly JobStateTransition Completed = new("完成文書作成待ち", "review_completed", JobRunProgressPlan.ReviewSucceeded);
    public static readonly JobStateTransition Skipped = new("素起こし完了", "review_skipped", JobRunProgressPlan.Completed);

    public static JobStateTransition AfterCandidateProcessing(int pendingDraftCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pendingDraftCount);

        return pendingDraftCount > 0 ? Ready : Completed;
    }
}
