using KoeNote.App.Models;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public sealed class CorrectionDraftRepository(AppPaths paths)
{
    public void SaveDrafts(IReadOnlyList<CorrectionDraft> drafts)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        ReplaceDrafts(drafts[0].JobId, drafts);
    }

    public void ReplaceDrafts(string jobId, IReadOnlyList<CorrectionDraft> drafts)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM correction_drafts WHERE job_id = $job_id;";
            deleteCommand.Parameters.AddWithValue("$job_id", jobId);
            deleteCommand.ExecuteNonQuery();
        }

        using (var segmentResetCommand = connection.CreateCommand())
        {
            segmentResetCommand.Transaction = transaction;
            segmentResetCommand.CommandText = "UPDATE transcript_segments SET review_state = 'none' WHERE job_id = $job_id;";
            segmentResetCommand.Parameters.AddWithValue("$job_id", jobId);
            segmentResetCommand.ExecuteNonQuery();
        }

        foreach (var draft in drafts)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO correction_drafts (
                    draft_id,
                    job_id,
                    segment_id,
                    issue_type,
                    original_text,
                    suggested_text,
                    reason,
                    confidence,
                    status,
                    created_at
                )
                VALUES (
                    $draft_id,
                    $job_id,
                    $segment_id,
                    $issue_type,
                    $original_text,
                    $suggested_text,
                    $reason,
                    $confidence,
                    $status,
                    $created_at
                )
                ON CONFLICT(draft_id) DO UPDATE SET
                    issue_type = excluded.issue_type,
                    original_text = excluded.original_text,
                    suggested_text = excluded.suggested_text,
                    reason = excluded.reason,
                    confidence = excluded.confidence,
                    status = excluded.status;
                """;
            command.Parameters.AddWithValue("$draft_id", draft.DraftId);
            command.Parameters.AddWithValue("$job_id", draft.JobId);
            command.Parameters.AddWithValue("$segment_id", draft.SegmentId);
            command.Parameters.AddWithValue("$issue_type", draft.IssueType);
            command.Parameters.AddWithValue("$original_text", draft.OriginalText);
            command.Parameters.AddWithValue("$suggested_text", draft.SuggestedText);
            command.Parameters.AddWithValue("$reason", draft.Reason);
            command.Parameters.AddWithValue("$confidence", draft.Confidence);
            command.Parameters.AddWithValue("$status", draft.Status);
            command.Parameters.AddWithValue("$created_at", (draft.CreatedAt ?? DateTimeOffset.Now).ToString("o"));
            command.ExecuteNonQuery();

            using var segmentCommand = connection.CreateCommand();
            segmentCommand.Transaction = transaction;
            segmentCommand.CommandText = """
                UPDATE transcript_segments
                SET review_state = 'has_draft'
                WHERE job_id = $job_id AND segment_id = $segment_id;
                """;
            segmentCommand.Parameters.AddWithValue("$job_id", draft.JobId);
            segmentCommand.Parameters.AddWithValue("$segment_id", draft.SegmentId);
            segmentCommand.ExecuteNonQuery();
        }

        using var jobCommand = connection.CreateCommand();
        jobCommand.Transaction = transaction;
        jobCommand.CommandText = """
            UPDATE jobs
            SET unreviewed_draft_count = $draft_count,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        jobCommand.Parameters.AddWithValue("$job_id", jobId);
        jobCommand.Parameters.AddWithValue("$draft_count", drafts.Count(static draft => draft.Status == "pending"));
        jobCommand.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        jobCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }
}
