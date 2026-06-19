using KoeNote.App.Services.Jobs;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public static class ReviewOperationPersistence
{
    public static string UpsertDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string draftId,
        string action,
        string? finalText,
        string? manualNote)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO review_decisions (
                decision_id,
                draft_id,
                action,
                final_text,
                manual_note,
                decided_at
            )
            VALUES (
                $decision_id,
                $draft_id,
                $action,
                $final_text,
                $manual_note,
                $decided_at
            );
            """;
        var decisionId = Guid.NewGuid().ToString("N");
        command.Parameters.AddWithValue("$decision_id", decisionId);
        command.Parameters.AddWithValue("$draft_id", draftId);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$final_text", (object?)finalText ?? DBNull.Value);
        command.Parameters.AddWithValue("$manual_note", (object?)manualNote ?? DBNull.Value);
        command.Parameters.AddWithValue("$decided_at", DateTimeOffset.Now.ToString("o"));
        var updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"Review decision could not be recorded: {draftId}");
        }

        return decisionId;
    }

    public static void UpdateSegment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId,
        string? finalText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = finalText is null
            ? """
              UPDATE transcript_segments
              SET review_state = CASE
                      WHEN EXISTS (
                          SELECT 1
                          FROM correction_drafts
                          WHERE job_id = $job_id
                              AND segment_id = $segment_id
                              AND status = 'pending'
                      ) THEN 'has_draft'
                      ELSE 'reviewed'
                  END
              WHERE job_id = $job_id AND segment_id = $segment_id;
              """
            : """
              UPDATE transcript_segments
              SET final_text = $final_text,
                  review_state = CASE
                      WHEN EXISTS (
                          SELECT 1
                          FROM correction_drafts
                          WHERE job_id = $job_id
                              AND segment_id = $segment_id
                              AND status = 'pending'
                      ) THEN 'has_draft'
                      ELSE 'reviewed'
                  END
              WHERE job_id = $job_id AND segment_id = $segment_id;
              """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        if (finalText is not null)
        {
            command.Parameters.AddWithValue("$final_text", finalText);
        }

        var updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"Transcript segment was not found: {jobId}/{segmentId}");
        }
    }

    public static void SetSegment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId,
        string? finalText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE transcript_segments
            SET final_text = $final_text,
                review_state = CASE
                    WHEN EXISTS (
                        SELECT 1
                        FROM correction_drafts
                        WHERE job_id = $job_id
                            AND segment_id = $segment_id
                            AND status = 'pending'
                    ) THEN 'has_draft'
                    ELSE 'reviewed'
                END
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        command.Parameters.AddWithValue("$final_text", (object?)finalText ?? DBNull.Value);
        var updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"Transcript segment was not found: {jobId}/{segmentId}");
        }
    }

    public static void RefreshJobPendingCount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        bool requiresReadableRefresh)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            WITH pending(value) AS (
                SELECT COUNT(*)
                FROM correction_drafts
                WHERE job_id = $job_id AND status = 'pending'
            )
            UPDATE jobs
            SET unreviewed_draft_count = (SELECT value FROM pending),
                status = CASE
                    WHEN (SELECT value FROM pending) = 0
                        AND ($requires_readable_refresh = 1 OR COALESCE(current_stage, '') <> 'readable_polishing_completed')
                        THEN '完成文書作成待ち'
                    ELSE status
                END,
                current_stage = CASE
                    WHEN (SELECT value FROM pending) = 0
                        AND ($requires_readable_refresh = 1 OR COALESCE(current_stage, '') <> 'readable_polishing_completed')
                        THEN 'review_completed'
                    ELSE current_stage
                END,
                progress_percent = CASE
                    WHEN (SELECT value FROM pending) = 0
                        AND ($requires_readable_refresh = 1 OR COALESCE(current_stage, '') <> 'readable_polishing_completed')
                        THEN $progress_percent
                    ELSE progress_percent
                END,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$progress_percent", JobRunProgressPlan.ReviewSucceeded);
        command.Parameters.AddWithValue("$requires_readable_refresh", requiresReadableRefresh ? 1 : 0);
        var updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"Job was not found while refreshing pending draft count: {jobId}");
        }
    }
}
