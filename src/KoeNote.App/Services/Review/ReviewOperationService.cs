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
        return Decide(draftId, "rejected", "rejected", static _ => null, null);
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
        Func<DraftSnapshot, string?> finalTextSelector,
        string? manualNote)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            throw new ArgumentException("Draft id is required.", nameof(draftId));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var draft = LoadDraft(connection, transaction, draftId)
            ?? throw new KeyNotFoundException($"Correction draft was not found: {draftId}");
        if (!string.Equals(draft.Status, "pending", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Correction draft has already been decided: {draftId}");
        }

        var before = LoadHistorySnapshot(connection, transaction, draft)
            ?? throw new InvalidOperationException($"Transcript segment was not found: {draft.JobId}/{draft.SegmentId}");
        var selectedText = finalTextSelector(draft);
        var finalText = selectedText is null ? null : BuildSegmentFinalText(draft, selectedText);
        UpdateDraftStatus(connection, transaction, draftId, draftStatus);
        var decisionId = UpsertDecision(connection, transaction, draftId, action, finalText, manualNote);
        UpdateSegment(connection, transaction, draft.JobId, draft.SegmentId, finalText);
        RefreshJobPendingCount(connection, transaction, draft.JobId);
        var afterSnapshot = LoadHistorySnapshot(connection, transaction, draft)
            ?? throw new InvalidOperationException($"Transcript segment was not found after review operation: {draft.JobId}/{draft.SegmentId}");
        var after = afterSnapshot with { DecisionId = decisionId };
        TranscriptEditService.InsertHistory(
            connection,
            transaction,
            draft.JobId,
            draftId,
            draft.SegmentId,
            "review_decision",
            before,
            after);

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
            SELECT
                d.job_id,
                d.segment_id,
                d.original_text,
                d.suggested_text,
                d.status,
                COALESCE(s.final_text, s.normalized_text, s.raw_text)
            FROM correction_drafts d
            JOIN transcript_segments s
                ON s.job_id = d.job_id AND s.segment_id = d.segment_id
            WHERE d.draft_id = $draft_id;
            """;
        command.Parameters.AddWithValue("$draft_id", draftId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DraftSnapshot(
            draftId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5));
    }

    private static string BuildSegmentFinalText(DraftSnapshot draft, string selectedText)
    {
        if (string.Equals(draft.CurrentSegmentText, draft.OriginalText, StringComparison.Ordinal))
        {
            return selectedText;
        }

        return draft.CurrentSegmentText.Contains(draft.OriginalText, StringComparison.Ordinal)
            ? draft.CurrentSegmentText.Replace(draft.OriginalText, selectedText, StringComparison.Ordinal)
            : selectedText;
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
        var updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"Correction draft could not be updated: {draftId}");
        }
    }

    private static string UpsertDecision(
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

    private static void UpdateSegment(
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
        var updated = command.ExecuteNonQuery();
        if (updated != 1)
        {
            throw new InvalidOperationException($"Job was not found while refreshing pending draft count: {jobId}");
        }
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

    private static TranscriptEditService.ReviewDecisionHistorySnapshot? LoadHistorySnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftSnapshot draft)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT
                d.status,
                s.final_text,
                s.review_state,
                j.unreviewed_draft_count
            FROM correction_drafts d
            JOIN transcript_segments s
                ON s.job_id = d.job_id AND s.segment_id = d.segment_id
            JOIN jobs j
                ON j.job_id = d.job_id
            WHERE d.draft_id = $draft_id;
            """;
        command.Parameters.AddWithValue("$draft_id", draft.DraftId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new TranscriptEditService.ReviewDecisionHistorySnapshot(
            draft.JobId,
            draft.SegmentId,
            draft.DraftId,
            reader.GetString(0),
            reader.IsDBNull(1) ? null : reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3));
    }

    private sealed record DraftSnapshot(
        string DraftId,
        string JobId,
        string SegmentId,
        string OriginalText,
        string SuggestedText,
        string Status,
        string CurrentSegmentText);
}
