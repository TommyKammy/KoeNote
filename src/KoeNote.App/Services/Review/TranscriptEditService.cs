using System.Text.Json;
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

        var before = LoadSegmentSnapshot(connection, transaction, jobId, segmentId)
            ?? throw new KeyNotFoundException($"Transcript segment was not found: {jobId}/{segmentId}");
        var after = before with { FinalText = finalText, ReviewState = "manually_edited" };

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE transcript_segments
            SET final_text = $final_text,
                review_state = $review_state
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        command.Parameters.AddWithValue("$final_text", finalText);
        command.Parameters.AddWithValue("$review_state", after.ReviewState);
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

    public void ApplySpeakerAlias(string jobId, string speakerId, string displayName)
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

        var before = LoadSpeakerAliasSnapshot(connection, transaction, jobId, speakerId);
        var after = new SpeakerAliasSnapshot(jobId, speakerId, displayName, Exists: true);

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
            VALUES ($job_id, $speaker_id, $display_name, $updated_at)
            ON CONFLICT(job_id, speaker_id) DO UPDATE SET
                display_name = excluded.display_name,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();

        InsertHistory(
            connection,
            transaction,
            jobId,
            draftId: null,
            segmentId: null,
            "speaker_alias",
            before,
            after);

        transaction.Commit();
    }

    public bool UndoLast(string? jobId = null)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        var operation = LoadLastOperation(connection, transaction, jobId);
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
            case "speaker_alias":
                UndoSpeakerAlias(connection, transaction, operation);
                break;
            default:
                throw new InvalidOperationException($"Unknown operation type: {operation.OperationType}");
        }

        DeleteHistory(connection, transaction, operation.OperationId);
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
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
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
            """;
        command.Parameters.AddWithValue("$operation_id", Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$draft_id", (object?)draftId ?? DBNull.Value);
        command.Parameters.AddWithValue("$segment_id", (object?)segmentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$operation_type", operationType);
        command.Parameters.AddWithValue("$before_json", JsonSerializer.Serialize(before));
        command.Parameters.AddWithValue("$after_json", JsonSerializer.Serialize(after));
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static SegmentSnapshot? LoadSegmentSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT job_id, segment_id, final_text, review_state
            FROM transcript_segments
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new SegmentSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetString(3));
    }

    private static SpeakerAliasSnapshot LoadSpeakerAliasSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string speakerId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT display_name
            FROM speaker_aliases
            WHERE job_id = $job_id AND speaker_id = $speaker_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);

        var displayName = command.ExecuteScalar() as string;
        return new SpeakerAliasSnapshot(jobId, speakerId, displayName, displayName is not null);
    }

    private static OperationSnapshot? LoadLastOperation(SqliteConnection connection, SqliteTransaction transaction, string? jobId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.IsNullOrWhiteSpace(jobId)
            ? """
              SELECT operation_id, job_id, draft_id, segment_id, operation_type, before_json, after_json
              FROM review_operation_history
              ORDER BY rowid DESC
              LIMIT 1;
              """
            : """
              SELECT operation_id, job_id, draft_id, segment_id, operation_type, before_json, after_json
              FROM review_operation_history
              WHERE job_id = $job_id
              ORDER BY rowid DESC
              LIMIT 1;
              """;
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            command.Parameters.AddWithValue("$job_id", jobId);
        }

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new OperationSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6));
    }

    private static void UndoReviewDecision(SqliteConnection connection, SqliteTransaction transaction, OperationSnapshot operation)
    {
        var before = JsonSerializer.Deserialize<ReviewDecisionHistorySnapshot>(operation.BeforeJson)
            ?? throw new InvalidOperationException("Invalid review decision undo snapshot.");
        var after = JsonSerializer.Deserialize<ReviewDecisionHistorySnapshot>(operation.AfterJson)
            ?? throw new InvalidOperationException("Invalid review decision redo snapshot.");

        using (var deleteDecision = connection.CreateCommand())
        {
            deleteDecision.Transaction = transaction;
            deleteDecision.CommandText = "DELETE FROM review_decisions WHERE decision_id = $decision_id;";
            deleteDecision.Parameters.AddWithValue("$decision_id", after.DecisionId);
            deleteDecision.ExecuteNonQuery();
        }

        using (var draftCommand = connection.CreateCommand())
        {
            draftCommand.Transaction = transaction;
            draftCommand.CommandText = "UPDATE correction_drafts SET status = $status WHERE draft_id = $draft_id;";
            draftCommand.Parameters.AddWithValue("$status", before.DraftStatus);
            draftCommand.Parameters.AddWithValue("$draft_id", before.DraftId);
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
            command.Parameters.AddWithValue("$display_name", before.DisplayName);
            command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        }
        else
        {
            command.CommandText = "DELETE FROM speaker_aliases WHERE job_id = $job_id AND speaker_id = $speaker_id;";
        }

        command.Parameters.AddWithValue("$job_id", before.JobId);
        command.Parameters.AddWithValue("$speaker_id", before.SpeakerId);
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
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE transcript_segments
            SET final_text = $final_text,
                review_state = $review_state
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        command.Parameters.AddWithValue("$final_text", (object?)finalText ?? DBNull.Value);
        command.Parameters.AddWithValue("$review_state", reviewState);
        command.ExecuteNonQuery();
    }

    private static void RestoreJobPendingCount(SqliteConnection connection, SqliteTransaction transaction, string jobId, int pendingCount)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE jobs
            SET unreviewed_draft_count = $pending_count,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$pending_count", pendingCount);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void DeleteHistory(SqliteConnection connection, SqliteTransaction transaction, string operationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM review_operation_history WHERE operation_id = $operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId);
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

    private sealed record SegmentSnapshot(
        string JobId,
        string SegmentId,
        string? FinalText,
        string ReviewState);

    private sealed record SpeakerAliasSnapshot(
        string JobId,
        string SpeakerId,
        string? DisplayName,
        bool Exists);

    private sealed record OperationSnapshot(
        string OperationId,
        string JobId,
        string? DraftId,
        string? SegmentId,
        string OperationType,
        string BeforeJson,
        string AfterJson);
}
