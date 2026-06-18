using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Review;

internal static class TranscriptEditHistoryStore
{
    public static SegmentSnapshot? LoadSegmentSnapshot(
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

    public static SpeakerAliasSnapshot LoadSpeakerAliasSnapshot(
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

    public static RawSegmentSnapshot? LoadRawSegmentSnapshot(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT s.job_id,
                   s.segment_id,
                   s.raw_text,
                   s.normalized_text,
                   s.final_text,
                   s.review_state,
                   j.unreviewed_draft_count,
                   j.status,
                   j.current_stage,
                   j.progress_percent
            FROM transcript_segments s
            JOIN jobs j ON j.job_id = s.job_id
            WHERE s.job_id = $job_id AND s.segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new RawSegmentSnapshot(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            LoadPendingDraftIds(connection, transaction, jobId, segmentId),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetInt32(9));
    }

    public static OperationSnapshot? LoadLastOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string? jobId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = string.IsNullOrWhiteSpace(jobId)
            ? """
              SELECT rowid, operation_id, job_id, draft_id, segment_id, operation_type, before_json, after_json, created_at
              FROM review_operation_history
              ORDER BY created_at DESC, rowid DESC
              LIMIT 1;
              """
            : """
              SELECT rowid, operation_id, job_id, draft_id, segment_id, operation_type, before_json, after_json, created_at
              FROM review_operation_history
              WHERE job_id = $job_id
              ORDER BY created_at DESC, rowid DESC
              LIMIT 1;
              """;
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            command.Parameters.AddWithValue("$job_id", jobId);
        }

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadOperationSnapshot(reader) : null;
    }

    public static OperationSnapshot? LoadLastSegmentOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT rowid, operation_id, job_id, draft_id, segment_id, operation_type, before_json, after_json, created_at
            FROM review_operation_history
            WHERE job_id = $job_id
              AND segment_id = $segment_id
            ORDER BY created_at DESC, rowid DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadOperationSnapshot(reader) : null;
    }

    public static void DeleteHistory(SqliteConnection connection, SqliteTransaction transaction, string operationId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM review_operation_history WHERE operation_id = $operation_id;";
        command.Parameters.AddWithValue("$operation_id", operationId);
        command.ExecuteNonQuery();
    }

    public static bool HasLaterJobStateOperation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string createdAt,
        long rowId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM review_operation_history
            WHERE job_id = $job_id
              AND operation_type IN ('review_decision', 'segment_edit', 'raw_segment_edit')
              AND (
                  created_at > $created_at
                  OR (created_at = $created_at AND rowid > $rowid)
              )
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$created_at", createdAt);
        command.Parameters.AddWithValue("$rowid", rowId);
        return command.ExecuteScalar() is not null;
    }

    private static IReadOnlyList<string> LoadPendingDraftIds(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string jobId,
        string segmentId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT draft_id
            FROM correction_drafts
            WHERE job_id = $job_id
              AND segment_id = $segment_id
              AND status = 'pending';
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);

        using var reader = command.ExecuteReader();
        var draftIds = new List<string>();
        while (reader.Read())
        {
            draftIds.Add(reader.GetString(0));
        }

        return draftIds;
    }

    private static OperationSnapshot ReadOperationSnapshot(SqliteDataReader reader)
    {
        return new OperationSnapshot(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.GetString(7),
            reader.GetString(8));
    }
}

internal sealed record SegmentSnapshot(
    string JobId,
    string SegmentId,
    string? FinalText,
    string ReviewState);

internal sealed record RawSegmentSnapshot(
    string JobId,
    string SegmentId,
    string RawText,
    string? NormalizedText,
    string? FinalText,
    string ReviewState,
    IReadOnlyList<string> PendingDraftIds,
    int JobPendingDraftCount,
    string JobStatus,
    string? JobCurrentStage,
    int JobProgressPercent);

internal sealed record SpeakerAliasSnapshot(
    string JobId,
    string SpeakerId,
    string? DisplayName,
    bool Exists);

internal sealed record OperationSnapshot(
    long RowId,
    string OperationId,
    string JobId,
    string? DraftId,
    string? SegmentId,
    string OperationType,
    string BeforeJson,
    string AfterJson,
    string CreatedAt);
