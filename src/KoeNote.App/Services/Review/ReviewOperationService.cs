using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public sealed class ReviewOperationService(AppPaths paths)
{
    public ReviewOperationResult AcceptDraft(string draftId)
    {
        return Decide(draftId, "accepted", "accepted", static draft => draft.SuggestedText, null, requiresReadableRefresh: true);
    }

    public ReviewOperationResult RejectDraft(string draftId)
    {
        return Decide(draftId, "rejected", "rejected", static _ => null, null, requiresReadableRefresh: false);
    }

    public ReviewOperationResult ApplyManualEdit(string draftId, string finalText, string? manualNote = null)
    {
        if (string.IsNullOrWhiteSpace(finalText))
        {
            throw new ArgumentException("Final text is required.", nameof(finalText));
        }

        return Decide(draftId, "edited", "manual_edit", _ => finalText, manualNote, requiresReadableRefresh: true);
    }

    public ReviewOperationResult ChangeToAccepted(string draftId)
    {
        return ChangeDecision(draftId, "accepted", "accepted", static draft => draft.SuggestedText, null, requiresReadableRefresh: true);
    }

    public ReviewOperationResult ChangeToRejected(string draftId)
    {
        return ChangeDecision(draftId, "rejected", "rejected", static _ => null, null, requiresReadableRefresh: false);
    }

    public ReviewOperationResult ChangeToManualEdit(string draftId, string finalText, string? manualNote = null)
    {
        if (string.IsNullOrWhiteSpace(finalText))
        {
            throw new ArgumentException("Final text is required.", nameof(finalText));
        }

        return ChangeDecision(draftId, "edited", "manual_edit", _ => finalText, manualNote, requiresReadableRefresh: true);
    }

    private ReviewOperationResult Decide(
        string draftId,
        string draftStatus,
        string action,
        Func<DraftSnapshot, string?> finalTextSelector,
        string? manualNote,
        bool requiresReadableRefresh)
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
        var decisionId = ReviewOperationPersistence.UpsertDecision(connection, transaction, draftId, action, finalText, manualNote);
        ReviewOperationPersistence.UpdateSegment(connection, transaction, draft.JobId, draft.SegmentId, finalText);
        ReviewOperationPersistence.RefreshJobPendingCount(connection, transaction, draft.JobId, requiresReadableRefresh);
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

    private ReviewOperationResult ChangeDecision(
        string draftId,
        string draftStatus,
        string action,
        Func<DraftSnapshot, string?> selectedTextSelector,
        string? manualNote,
        bool requiresReadableRefresh)
    {
        if (string.IsNullOrWhiteSpace(draftId))
        {
            throw new ArgumentException("Draft id is required.", nameof(draftId));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var draft = LoadDraft(connection, transaction, draftId)
            ?? throw new KeyNotFoundException($"Correction draft was not found: {draftId}");
        if (string.Equals(draft.Status, "pending", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Correction draft is still pending and cannot be changed: {draftId}");
        }

        var baseline = LoadEarliestDecisionBeforeSnapshot(connection, transaction, draft)
            ?? throw new InvalidOperationException($"Review decision history is required before changing a decision: {draftId}");
        EnsureLatestSegmentOperationIsDraftDecision(connection, transaction, draft);

        var before = LoadHistorySnapshot(connection, transaction, draft)
            ?? throw new InvalidOperationException($"Transcript segment was not found: {draft.JobId}/{draft.SegmentId}");
        var selectedText = selectedTextSelector(draft);
        var finalText = selectedText is null
            ? baseline.SegmentFinalText
            : BuildSegmentFinalText(
                LoadBaselineSegmentText(connection, transaction, draft.JobId, draft.SegmentId, baseline.SegmentFinalText),
                draft.OriginalText,
                selectedText);

        UpdateDraftStatus(connection, transaction, draftId, draftStatus);
        var decisionId = ReviewOperationPersistence.UpsertDecision(connection, transaction, draftId, action, finalText, manualNote);
        ReviewOperationPersistence.SetSegment(connection, transaction, draft.JobId, draft.SegmentId, finalText);
        ReviewOperationPersistence.RefreshJobPendingCount(connection, transaction, draft.JobId, requiresReadableRefresh);
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
        return BuildSegmentFinalText(draft.CurrentSegmentText, draft.OriginalText, selectedText);
    }

    private static string BuildSegmentFinalText(string currentSegmentText, string originalText, string selectedText)
    {
        if (string.Equals(currentSegmentText, originalText, StringComparison.Ordinal))
        {
            return selectedText;
        }

        var index = currentSegmentText.IndexOf(originalText, StringComparison.Ordinal);
        if (index < 0)
        {
            return selectedText;
        }

        return string.Concat(
            currentSegmentText.AsSpan(0, index),
            selectedText,
            currentSegmentText.AsSpan(index + originalText.Length));
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

    private static TranscriptEditService.ReviewDecisionHistorySnapshot? LoadEarliestDecisionBeforeSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftSnapshot draft)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT before_json
            FROM review_operation_history
            WHERE draft_id = $draft_id
              AND operation_type = 'review_decision'
            ORDER BY created_at ASC, rowid ASC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$draft_id", draft.DraftId);

        var beforeJson = command.ExecuteScalar() as string;
        return beforeJson is null
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<TranscriptEditService.ReviewDecisionHistorySnapshot>(beforeJson);
    }

    private static void EnsureLatestSegmentOperationIsDraftDecision(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftSnapshot draft)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT draft_id, operation_type
            FROM review_operation_history
            WHERE job_id = $job_id
              AND segment_id = $segment_id
            ORDER BY created_at DESC, rowid DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", draft.JobId);
        command.Parameters.AddWithValue("$segment_id", draft.SegmentId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            throw new InvalidOperationException($"Review decision history is required before changing a decision: {draft.DraftId}");
        }

        var latestDraftId = reader.IsDBNull(0) ? null : reader.GetString(0);
        var operationType = reader.GetString(1);
        if (!string.Equals(operationType, "review_decision", StringComparison.Ordinal) ||
            !string.Equals(latestDraftId, draft.DraftId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "This decision cannot be changed because a newer operation exists on the same transcript segment.");
        }
    }

    private static string LoadBaselineSegmentText(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId,
        string? baselineFinalText)
    {
        if (baselineFinalText is not null)
        {
            return baselineFinalText;
        }

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(normalized_text, raw_text)
            FROM transcript_segments
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        var value = command.ExecuteScalar() as string;
        if (value is null)
        {
            throw new InvalidOperationException($"Transcript segment was not found: {jobId}/{segmentId}");
        }

        return value;
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
