using KoeNote.App.Models;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Asr;

public sealed class TranscriptSegmentRepository(AppPaths paths)
{
    public IReadOnlyList<TranscriptSegment> ReadSegments(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        var segments = new TranscriptReadRepository(paths).ReadForJob(jobId);
        return segments
            .Select(segment => new TranscriptSegment(
                segment.SegmentId,
                jobId,
                segment.StartSeconds,
                segment.EndSeconds,
                string.IsNullOrWhiteSpace(segment.SpeakerId) ? null : segment.SpeakerId,
                segment.RawText,
                segment.NormalizedText))
            .ToArray();
    }

    public IReadOnlyList<TranscriptSegmentPreview> ReadPreviews(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        var segments = new TranscriptReadRepository(paths).ReadForJob(jobId);
        var previews = new List<TranscriptSegmentPreview>();
        foreach (var segment in segments)
        {
            previews.Add(new TranscriptSegmentPreview(
                TimestampFormatter.FormatDisplay(segment.StartSeconds),
                TimestampFormatter.FormatDisplay(segment.EndSeconds),
                segment.Speaker,
                segment.Text,
                FormatReviewState(segment.ReviewState),
                segment.SegmentId,
                segment.SpeakerId,
                segment.RawText,
                segment.NormalizedText,
                segment.FinalText,
                segment.StartSeconds,
                segment.EndSeconds));
        }

        return previews;
    }

    public void SaveSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        SaveSegments(connection, transaction, segments);
        transaction.Commit();
    }

    public void ReplaceSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0)
        {
            throw new ArgumentException("Replacement without a job id requires at least one segment.", nameof(segments));
        }

        var jobIds = segments
            .Select(static segment => segment.JobId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (jobIds.Length != 1)
        {
            throw new ArgumentException("Replacement requires segments for exactly one job.", nameof(segments));
        }

        ReplaceSegments(jobIds[0], segments);
    }

    public void ReplaceSegments(string jobId, IReadOnlyList<TranscriptSegment> segments)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("A job id is required for replacement.", nameof(jobId));
        }

        if (segments.Any(segment => !string.Equals(segment.JobId, jobId, StringComparison.Ordinal)))
        {
            throw new ArgumentException("All replacement segments must belong to the specified job.", nameof(segments));
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();
        DeleteExistingTranscriptForJob(connection, transaction, jobId);
        SaveSegments(connection, transaction, segments);
        transaction.Commit();
    }

    private static void SaveSegments(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        IReadOnlyList<TranscriptSegment> segments)
    {

        foreach (var segment in segments)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO transcript_segments (
                    segment_id,
                    job_id,
                    start_seconds,
                    end_seconds,
                    speaker_id,
                    raw_text,
                    normalized_text,
                    asr_confidence,
                    source,
                    asr_run_id
                )
                VALUES (
                    $segment_id,
                    $job_id,
                    $start_seconds,
                    $end_seconds,
                    $speaker_id,
                    $raw_text,
                    $normalized_text,
                    $asr_confidence,
                    $source,
                    $asr_run_id
                )
                ON CONFLICT(job_id, segment_id) DO UPDATE SET
                    start_seconds = excluded.start_seconds,
                    end_seconds = excluded.end_seconds,
                    speaker_id = excluded.speaker_id,
                    raw_text = excluded.raw_text,
                    normalized_text = excluded.normalized_text,
                    asr_confidence = excluded.asr_confidence,
                    source = excluded.source,
                    asr_run_id = excluded.asr_run_id;
                """;
            command.Parameters.AddWithValue("$segment_id", segment.SegmentId);
            command.Parameters.AddWithValue("$job_id", segment.JobId);
            command.Parameters.AddWithValue("$start_seconds", segment.StartSeconds);
            command.Parameters.AddWithValue("$end_seconds", segment.EndSeconds);
            command.Parameters.AddWithValue("$speaker_id", (object?)segment.SpeakerId ?? DBNull.Value);
            command.Parameters.AddWithValue("$raw_text", segment.RawText);
            command.Parameters.AddWithValue("$normalized_text", (object?)segment.NormalizedText ?? DBNull.Value);
            command.Parameters.AddWithValue("$asr_confidence", (object?)segment.AsrConfidence ?? DBNull.Value);
            command.Parameters.AddWithValue("$source", segment.Source);
            command.Parameters.AddWithValue("$asr_run_id", (object?)segment.AsrRunId ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    private static void DeleteExistingTranscriptForJob(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        Microsoft.Data.Sqlite.SqliteTransaction transaction,
        string jobId)
    {
        using (var decisionCommand = connection.CreateCommand())
        {
            decisionCommand.Transaction = transaction;
            decisionCommand.CommandText = """
                DELETE FROM review_decisions
                WHERE draft_id IN (
                    SELECT draft_id FROM correction_drafts WHERE job_id = $job_id
                );
                """;
            decisionCommand.Parameters.AddWithValue("$job_id", jobId);
            decisionCommand.ExecuteNonQuery();
        }

        foreach (var sql in new[]
        {
            "DELETE FROM review_operation_history WHERE job_id = $job_id;",
            "DELETE FROM correction_drafts WHERE job_id = $job_id;",
            "DELETE FROM transcript_segments WHERE job_id = $job_id;"
        })
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$job_id", jobId);
            command.ExecuteNonQuery();
        }
    }

    private static string FormatReviewState(string state)
    {
        return state switch
        {
            "has_draft" => "整文候補あり",
            "reviewed" => "整文済み",
            "manually_edited" => "手修正済み",
            _ => "候補なし"
        };
    }
}
