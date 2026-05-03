using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public sealed class ReviewOperationService(AppPaths paths)
{
    public ReviewOperationResult AcceptDraft(string draftId)
    {
        return Decide(draftId, "accepted", "accepted", static draft => draft.SuggestedText, null);
    }

    public ReviewOperationResult RejectDraft(string draftId)
    {
        return Decide(draftId, "rejected", "rejected", static draft => draft.OriginalText, null);
    }

    public ReviewOperationResult ApplyManualEdit(string draftId, string finalText, string? manualNote = null)
    {
        if (string.IsNullOrWhiteSpace(finalText))
        {
            throw new ArgumentException("Final text is required.", nameof(finalText));
        }

        return Decide(draftId, "edited", "manual_edit", _ => finalText, manualNote);
    }

    private ReviewOperationResult Decide(
        string draftId,
        string draftStatus,
        string action,
        Func<DraftSnapshot, string> finalTextSelector,
        string? manualNote)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            throw new ArgumentException("Draft id is required.", nameof(draftId));
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var draft = LoadDraft(connection, transaction, draftId)
            ?? throw new KeyNotFoundException($"Correction draft was not found: {draftId}");
        if (!string.Equals(draft.Status, "pending", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Correction draft has already been decided: {draftId}");
        }

        var finalText = finalTextSelector(draft);
        UpdateDraftStatus(connection, transaction, draftId, draftStatus);
        UpsertDecision(connection, transaction, draftId, action, finalText, manualNote);
        UpdateSegment(connection, transaction, draft.JobId, draft.SegmentId, finalText);
        RefreshJobPendingCount(connection, transaction, draft.JobId);

        transaction.Commit();

        return new ReviewOperationResult(
            draft.JobId,
            draft.SegmentId,
            draftId,
            action,
            finalText,
            CountPendingDrafts(connection, draft.JobId));
    }

    private static DraftSnapshot? LoadDraft(SqliteConnection connection, SqliteTransaction transaction, string draftId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT job_id, segment_id, original_text, suggested_text, status
            FROM correction_drafts
            WHERE draft_id = $draft_id;
            """;
        command.Parameters.AddWithValue("$draft_id", draftId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DraftSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4));
    }

    private static void UpdateDraftStatus(SqliteConnection connection, SqliteTransaction transaction, string draftId, string status)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE correction_drafts
            SET status = $status
            WHERE draft_id = $draft_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$draft_id", draftId);
        command.ExecuteNonQuery();
    }

    private static void UpsertDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string draftId,
        string action,
        string finalText,
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
        command.Parameters.AddWithValue("$decision_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$draft_id", draftId);
        command.Parameters.AddWithValue("$action", action);
        command.Parameters.AddWithValue("$final_text", finalText);
        command.Parameters.AddWithValue("$manual_note", (object?)manualNote ?? DBNull.Value);
        command.Parameters.AddWithValue("$decided_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void UpdateSegment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId,
        string finalText)
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
        command.Parameters.AddWithValue("$final_text", finalText);
        command.ExecuteNonQuery();
    }

    private static void RefreshJobPendingCount(SqliteConnection connection, SqliteTransaction transaction, string jobId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE jobs
            SET unreviewed_draft_count = (
                    SELECT COUNT(*)
                    FROM correction_drafts
                    WHERE job_id = $job_id AND status = 'pending'
                ),
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static int CountPendingDrafts(SqliteConnection connection, string jobId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM correction_drafts
            WHERE job_id = $job_id AND status = 'pending';
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        return Convert.ToInt32(command.ExecuteScalar());
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

    private sealed record DraftSnapshot(
        string JobId,
        string SegmentId,
        string OriginalText,
        string SuggestedText,
        string Status);
}
