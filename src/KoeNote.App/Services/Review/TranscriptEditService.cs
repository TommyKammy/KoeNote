using System.Text.Json;
using KoeNote.App.Services.Jobs;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

public sealed class TranscriptEditService(AppPaths paths)
{
    public void ApplySegmentEdit(string jobId, string segmentId, string finalText)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(segmentId))
        {
            throw new ArgumentException("Segment id is required.", nameof(segmentId));
        }

        if (string.IsNullOrWhiteSpace(finalText))
        {
            throw new ArgumentException("Final text is required.", nameof(finalText));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var before = TranscriptEditHistoryStore.LoadSegmentSnapshot(connection, transaction, jobId, segmentId)
            ?? throw new KeyNotFoundException($"Transcript segment was not found: {jobId}/{segmentId}");
        var after = before with { FinalText = finalText, ReviewState = "manually_edited" };

        using var command = connection.CreateCommand(transaction, """
            UPDATE transcript_segments
            SET final_text = $final_text,
                review_state = $review_state
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$segment_id", segmentId)
            .AddValue("$final_text", finalText)
            .AddValue("$review_state", after.ReviewState);
        command.ExecuteNonQuery();

        InsertHistory(
            connection,
            transaction,
            jobId,
            draftId: null,
            segmentId,
            "segment_edit",
            before,
            after);

        transaction.Commit();
    }

    public void ApplyRawSegmentEdit(string jobId, string segmentId, string rawText)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(segmentId))
        {
            throw new ArgumentException("Segment id is required.", nameof(segmentId));
        }

        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new ArgumentException("Raw text is required.", nameof(rawText));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var before = TranscriptEditHistoryStore.LoadRawSegmentSnapshot(connection, transaction, jobId, segmentId)
            ?? throw new KeyNotFoundException($"Transcript segment was not found: {jobId}/{segmentId}");
        var after = before with
        {
            RawText = rawText,
            NormalizedText = null,
            FinalText = null,
            ReviewState = "manually_edited",
            PendingDraftIds = []
        };

        using var command = connection.CreateCommand(transaction, """
            UPDATE transcript_segments
            SET raw_text = $raw_text,
                normalized_text = NULL,
                final_text = NULL,
                review_state = $review_state
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$segment_id", segmentId)
            .AddValue("$raw_text", rawText)
            .AddValue("$review_state", after.ReviewState);
        command.ExecuteNonQuery();

        InvalidatePendingDraftsForSegment(connection, transaction, jobId, segmentId);
        RefreshRawEditPendingCount(connection, transaction, jobId);

        InsertHistory(
            connection,
            transaction,
            jobId,
            draftId: null,
            segmentId,
            "raw_segment_edit",
            before,
            after);

        transaction.Commit();
    }

    public void ApplySpeakerAlias(string jobId, string speakerId, string displayName, bool recordHistory = true)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(speakerId))
        {
            throw new ArgumentException("Speaker id is required.", nameof(speakerId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Display name is required.", nameof(displayName));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var before = TranscriptEditHistoryStore.LoadSpeakerAliasSnapshot(connection, transaction, jobId, speakerId);
        var after = new SpeakerAliasSnapshot(jobId, speakerId, displayName, Exists: true);

        using var command = connection.CreateCommand(transaction, """
            INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
            VALUES ($job_id, $speaker_id, $display_name, $updated_at)
            ON CONFLICT(job_id, speaker_id) DO UPDATE SET
                display_name = excluded.display_name,
                updated_at = excluded.updated_at;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$speaker_id", speakerId)
            .AddValue("$display_name", displayName)
            .AddIsoDateTimeOffset("$updated_at", DateTimeOffset.Now);
        command.ExecuteNonQuery();

        if (recordHistory)
        {
            InsertHistory(
                connection,
                transaction,
                jobId,
                draftId: null,
                segmentId: null,
                "speaker_alias",
                before,
                after);
        }

        transaction.Commit();
    }

    public bool UndoLast(string? jobId = null)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var operation = TranscriptEditHistoryStore.LoadLastOperation(connection, transaction, jobId);
        if (operation is null)
        {
            return false;
        }

        switch (operation.OperationType)
        {
            case "review_decision":
                UndoReviewDecision(connection, transaction, operation);
                break;
            case "segment_edit":
                UndoSegmentEdit(connection, transaction, operation);
                break;
            case "raw_segment_edit":
                UndoRawSegmentEdit(connection, transaction, operation);
                break;
            case "speaker_alias":
                UndoSpeakerAlias(connection, transaction, operation);
                break;
            default:
                throw new InvalidOperationException($"Unknown operation type: {operation.OperationType}");
        }

        TranscriptEditHistoryStore.DeleteHistory(connection, transaction, operation.OperationId);
        transaction.Commit();
        return true;
    }

    public bool UndoLastSegmentEdit(string jobId, string segmentId)
    {
        return UndoLastSegmentEdit(jobId, segmentId, rawOnly: false);
    }

    public bool UndoLastRawSegmentEdit(string jobId, string segmentId)
    {
        return UndoLastSegmentEdit(jobId, segmentId, rawOnly: true);
    }

    private bool UndoLastSegmentEdit(string jobId, string segmentId, bool rawOnly)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(segmentId))
        {
            throw new ArgumentException("Segment id is required.", nameof(segmentId));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var operation = TranscriptEditHistoryStore.LoadLastSegmentOperation(connection, transaction, jobId, segmentId);
        if (operation is null)
        {
            return false;
        }

        if (rawOnly && operation.OperationType != "raw_segment_edit")
        {
            return false;
        }

        if (operation.OperationType is not ("segment_edit" or "raw_segment_edit"))
        {
            return false;
        }

        switch (operation.OperationType)
        {
            case "segment_edit":
                UndoSegmentEdit(connection, transaction, operation);
                break;
            case "raw_segment_edit":
                UndoRawSegmentEdit(connection, transaction, operation);
                break;
            default:
                throw new InvalidOperationException($"Unknown segment edit operation type: {operation.OperationType}");
        }

        TranscriptEditHistoryStore.DeleteHistory(connection, transaction, operation.OperationId);
        transaction.Commit();
        return true;
    }

    internal static void InsertHistory<TBefore, TAfter>(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string? draftId,
        string? segmentId,
        string operationType,
        TBefore before,
        TAfter after)
    {
        using var command = connection.CreateCommand(transaction, """
            INSERT INTO review_operation_history (
                operation_id,
                job_id,
                draft_id,
                segment_id,
                operation_type,
                before_json,
                after_json,
                created_at
            )
            VALUES (
                $operation_id,
                $job_id,
                $draft_id,
                $segment_id,
                $operation_type,
                $before_json,
                $after_json,
                $created_at
            );
            """);
        command
            .AddValue("$operation_id", Guid.NewGuid().ToString("N"))
            .AddValue("$job_id", jobId)
            .AddValue("$draft_id", draftId)
            .AddValue("$segment_id", segmentId)
            .AddValue("$operation_type", operationType)
            .AddValue("$before_json", JsonSerializer.Serialize(before))
            .AddValue("$after_json", JsonSerializer.Serialize(after))
            .AddIsoDateTimeOffset("$created_at", DateTimeOffset.Now);
        command.ExecuteNonQuery();
    }

    private static void UndoReviewDecision(SqliteConnection connection, SqliteTransaction transaction, OperationSnapshot operation)
    {
        var before = JsonSerializer.Deserialize<ReviewDecisionHistorySnapshot>(operation.BeforeJson)
            ?? throw new InvalidOperationException("Invalid review decision undo snapshot.");
        var after = JsonSerializer.Deserialize<ReviewDecisionHistorySnapshot>(operation.AfterJson)
            ?? throw new InvalidOperationException("Invalid review decision redo snapshot.");

        using (var deleteDecision = connection.CreateCommand(
            transaction,
            "DELETE FROM review_decisions WHERE decision_id = $decision_id;"))
        {
            deleteDecision.AddValue("$decision_id", after.DecisionId);
            deleteDecision.ExecuteNonQuery();
        }

        using (var draftCommand = connection.CreateCommand(
            transaction,
            "UPDATE correction_drafts SET status = $status WHERE draft_id = $draft_id;"))
        {
            draftCommand
                .AddValue("$status", before.DraftStatus)
                .AddValue("$draft_id", before.DraftId);
            draftCommand.ExecuteNonQuery();
        }

        RestoreSegment(connection, transaction, before.JobId, before.SegmentId, before.SegmentFinalText, before.SegmentReviewState);
        RestoreJobPendingCount(connection, transaction, before.JobId, before.JobPendingDraftCount);
    }

    private static void UndoSegmentEdit(SqliteConnection connection, SqliteTransaction transaction, OperationSnapshot operation)
    {
        var before = JsonSerializer.Deserialize<SegmentSnapshot>(operation.BeforeJson)
            ?? throw new InvalidOperationException("Invalid segment edit undo snapshot.");

        RestoreSegment(connection, transaction, before.JobId, before.SegmentId, before.FinalText, before.ReviewState);
    }

    private static void UndoRawSegmentEdit(SqliteConnection connection, SqliteTransaction transaction, OperationSnapshot operation)
    {
        var before = JsonSerializer.Deserialize<RawSegmentSnapshot>(operation.BeforeJson)
            ?? throw new InvalidOperationException("Invalid raw segment edit undo snapshot.");

        RestoreRawSegment(
            connection,
            transaction,
            before.JobId,
            before.SegmentId,
            before.RawText,
            before.NormalizedText,
            before.FinalText,
            before.ReviewState);
        InvalidatePendingDraftsForSegment(connection, transaction, before.JobId, before.SegmentId);
        RestorePendingDrafts(connection, transaction, before.PendingDraftIds);
        if (before.PendingDraftIds.Count == 0 &&
            before.JobPendingDraftCount == 0 &&
            CountPendingDrafts(connection, transaction, before.JobId) == 0 &&
            !TranscriptEditHistoryStore.HasLaterJobStateOperation(connection, transaction, before.JobId, operation.CreatedAt, operation.RowId))
        {
            RestoreRawEditJobState(
                connection,
                transaction,
                before.JobId,
                before.JobPendingDraftCount,
                before.JobStatus,
                before.JobCurrentStage,
                before.JobProgressPercent);
        }
        else
        {
            RefreshRawEditPendingCount(connection, transaction, before.JobId);
        }
    }

    private static void UndoSpeakerAlias(SqliteConnection connection, SqliteTransaction transaction, OperationSnapshot operation)
    {
        var before = JsonSerializer.Deserialize<SpeakerAliasSnapshot>(operation.BeforeJson)
            ?? throw new InvalidOperationException("Invalid speaker alias undo snapshot.");

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        if (before.Exists)
        {
            command.CommandText = """
                INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
                VALUES ($job_id, $speaker_id, $display_name, $updated_at)
                ON CONFLICT(job_id, speaker_id) DO UPDATE SET
                    display_name = excluded.display_name,
                    updated_at = excluded.updated_at;
                """;
            command
                .AddValue("$display_name", before.DisplayName)
                .AddIsoDateTimeOffset("$updated_at", DateTimeOffset.Now);
        }
        else
        {
            command.CommandText = "DELETE FROM speaker_aliases WHERE job_id = $job_id AND speaker_id = $speaker_id;";
        }

        command
            .AddValue("$job_id", before.JobId)
            .AddValue("$speaker_id", before.SpeakerId);
        command.ExecuteNonQuery();
    }

    private static void RestoreSegment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId,
        string? finalText,
        string reviewState)
    {
        using var command = connection.CreateCommand(transaction, """
            UPDATE transcript_segments
            SET final_text = $final_text,
                review_state = $review_state
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$segment_id", segmentId)
            .AddValue("$final_text", finalText)
            .AddValue("$review_state", reviewState);
        command.ExecuteNonQuery();
    }

    private static void RestoreRawSegment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId,
        string rawText,
        string? normalizedText,
        string? finalText,
        string reviewState)
    {
        using var command = connection.CreateCommand(transaction, """
            UPDATE transcript_segments
            SET raw_text = $raw_text,
                normalized_text = $normalized_text,
                final_text = $final_text,
                review_state = $review_state
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$segment_id", segmentId)
            .AddValue("$raw_text", rawText)
            .AddValue("$normalized_text", normalizedText)
            .AddValue("$final_text", finalText)
            .AddValue("$review_state", reviewState);
        command.ExecuteNonQuery();
    }

    private static void InvalidatePendingDraftsForSegment(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId)
    {
        using var command = connection.CreateCommand(transaction, """
            UPDATE correction_drafts
            SET status = 'invalidated'
            WHERE job_id = $job_id
              AND segment_id = $segment_id
              AND status = 'pending';
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$segment_id", segmentId);
        command.ExecuteNonQuery();
    }

    private static void RestorePendingDrafts(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IReadOnlyList<string> draftIds)
    {
        if (draftIds.Count == 0)
        {
            return;
        }

        using var command = connection.CreateCommand(transaction, """
            UPDATE correction_drafts
            SET status = 'pending'
            WHERE draft_id = $draft_id;
            """);
        var parameter = command.Parameters.Add("$draft_id", SqliteType.Text);

        foreach (var draftId in draftIds)
        {
            parameter.Value = draftId;
            command.ExecuteNonQuery();
        }
    }

    private static int CountPendingDrafts(SqliteConnection connection, SqliteTransaction transaction, string jobId)
    {
        using var command = connection.CreateCommand(transaction, """
            SELECT COUNT(*)
            FROM correction_drafts
            WHERE job_id = $job_id AND status = 'pending';
            """);
        command.AddValue("$job_id", jobId);
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void RefreshRawEditPendingCount(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId)
    {
        using var command = connection.CreateCommand(transaction, """
            WITH pending(value) AS (
                SELECT COUNT(*)
                FROM correction_drafts
                WHERE job_id = $job_id AND status = 'pending'
            )
            UPDATE jobs
            SET unreviewed_draft_count = (SELECT value FROM pending),
                status = CASE
                    WHEN (SELECT value FROM pending) > 0
                        THEN $review_ready_status
                    WHEN (SELECT value FROM pending) = 0
                        THEN '完成文書作成待ち'
                    ELSE status
                END,
                current_stage = CASE
                    WHEN (SELECT value FROM pending) > 0
                        THEN 'review_ready'
                    WHEN (SELECT value FROM pending) = 0
                        THEN 'review_completed'
                    ELSE current_stage
                END,
                progress_percent = CASE
                    WHEN (SELECT value FROM pending) > 0
                        THEN $progress_percent
                    WHEN (SELECT value FROM pending) = 0
                        THEN $progress_percent
                    ELSE progress_percent
                END,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$review_ready_status", ReviewCandidateJobStateRules.Ready.Status)
            .AddValue("$progress_percent", JobRunProgressPlan.ReviewSucceeded)
            .AddIsoDateTimeOffset("$updated_at", DateTimeOffset.Now);
        command.ExecuteNonQuery();
    }

    private static void RestoreRawEditJobState(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        int pendingCount,
        string status,
        string? currentStage,
        int progressPercent)
    {
        using var command = connection.CreateCommand(transaction, """
            UPDATE jobs
            SET unreviewed_draft_count = $pending_count,
                status = $status,
                current_stage = $current_stage,
                progress_percent = $progress_percent,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$pending_count", pendingCount)
            .AddValue("$status", status)
            .AddValue("$current_stage", currentStage)
            .AddValue("$progress_percent", progressPercent)
            .AddIsoDateTimeOffset("$updated_at", DateTimeOffset.Now);
        command.ExecuteNonQuery();
    }

    private static void RestoreJobPendingCount(SqliteConnection connection, SqliteTransaction transaction, string jobId, int pendingCount)
    {
        using var command = connection.CreateCommand(transaction, """
            UPDATE jobs
            SET unreviewed_draft_count = $pending_count,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """);
        command
            .AddValue("$job_id", jobId)
            .AddValue("$pending_count", pendingCount)
            .AddIsoDateTimeOffset("$updated_at", DateTimeOffset.Now);
        command.ExecuteNonQuery();
    }

    internal sealed record ReviewDecisionHistorySnapshot(
        string JobId,
        string SegmentId,
        string DraftId,
        string DraftStatus,
        string? SegmentFinalText,
        string SegmentReviewState,
        int JobPendingDraftCount,
        string? DecisionId = null);
}
