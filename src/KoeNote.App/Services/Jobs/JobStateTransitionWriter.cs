using KoeNote.App.Models;

namespace KoeNote.App.Services.Jobs;

internal sealed class JobStateTransitionWriter(AppPaths paths)
{
    public void Apply(
        JobSummary job,
        JobStateTransition state,
        string? normalizedAudioPath,
        string? errorCategory = null,
        int? unreviewedDraftCount = null)
    {
        Update(
            job,
            state.Status,
            state.CurrentStage,
            state.ProgressPercent,
            normalizedAudioPath,
            errorCategory,
            unreviewedDraftCount);
    }

    public void Update(
        JobSummary job,
        string status,
        string currentStage,
        int progressPercent,
        string? normalizedAudioPath,
        string? errorCategory = null,
        int? unreviewedDraftCount = null)
    {
        job.Status = status;
        job.ProgressPercent = progressPercent;
        job.NormalizedAudioPath = normalizedAudioPath;
        if (unreviewedDraftCount is not null)
        {
            job.UnreviewedDrafts = unreviewedDraftCount.Value;
        }

        job.UpdatedAt = DateTimeOffset.Now;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = $status,
                current_stage = $current_stage,
                progress_percent = $progress_percent,
                normalized_audio_path = $normalized_audio_path,
                unreviewed_draft_count = COALESCE($unreviewed_draft_count, unreviewed_draft_count),
                updated_at = $updated_at,
                last_error_category = $last_error_category
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddValue("$status", status);
        command.Parameters.AddValue("$current_stage", currentStage);
        command.Parameters.AddValue("$progress_percent", progressPercent);
        command.Parameters.AddValue("$normalized_audio_path", normalizedAudioPath);
        command.Parameters.AddValue("$unreviewed_draft_count", unreviewedDraftCount);
        command.Parameters.AddValue("$updated_at", job.UpdatedAt.ToString("o"));
        command.Parameters.AddValue("$last_error_category", errorCategory);
        command.Parameters.AddValue("$job_id", job.JobId);
        command.ExecuteNonQuery();
    }

    public void ApplyReviewSkippedAndClearDrafts(JobSummary job)
    {
        var skippedState = ReviewCandidateJobStateRules.Skipped;
        job.Status = skippedState.Status;
        job.ProgressPercent = skippedState.ProgressPercent;
        job.UnreviewedDrafts = 0;
        job.UpdatedAt = DateTimeOffset.Now;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        using (var jobCommand = connection.CreateCommand())
        {
            jobCommand.Transaction = transaction;
            jobCommand.CommandText = """
                UPDATE jobs
                SET status = $status,
                    current_stage = $current_stage,
                    progress_percent = $progress_percent,
                    normalized_audio_path = $normalized_audio_path,
                    unreviewed_draft_count = 0,
                    updated_at = $updated_at,
                    last_error_category = NULL
                WHERE job_id = $job_id;
                """;
            jobCommand.Parameters.AddValue("$status", job.Status);
            jobCommand.Parameters.AddValue("$current_stage", skippedState.CurrentStage);
            jobCommand.Parameters.AddValue("$progress_percent", skippedState.ProgressPercent);
            jobCommand.Parameters.AddValue("$normalized_audio_path", job.NormalizedAudioPath);
            jobCommand.Parameters.AddValue("$updated_at", job.UpdatedAt.ToString("o"));
            jobCommand.Parameters.AddValue("$job_id", job.JobId);
            jobCommand.ExecuteNonQuery();
        }

        using (var draftCommand = connection.CreateCommand())
        {
            draftCommand.Transaction = transaction;
            draftCommand.CommandText = """
                UPDATE correction_drafts
                SET status = 'skipped'
                WHERE job_id = $job_id AND status = 'pending';
                """;
            draftCommand.Parameters.AddValue("$job_id", job.JobId);
            draftCommand.ExecuteNonQuery();
        }

        using (var segmentCommand = connection.CreateCommand())
        {
            segmentCommand.Transaction = transaction;
            segmentCommand.CommandText = """
                UPDATE transcript_segments
                SET review_state = 'none'
                WHERE job_id = $job_id AND review_state = 'has_draft';
                """;
            segmentCommand.Parameters.AddValue("$job_id", job.JobId);
            segmentCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
