using KoeNote.App.Models;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Asr;

public sealed class TranscriptSegmentRepository(AppPaths paths)
{
    public void SaveSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

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
                    source
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
                    $source
                )
                ON CONFLICT(job_id, segment_id) DO UPDATE SET
                    start_seconds = excluded.start_seconds,
                    end_seconds = excluded.end_seconds,
                    speaker_id = excluded.speaker_id,
                    raw_text = excluded.raw_text,
                    normalized_text = excluded.normalized_text,
                    asr_confidence = excluded.asr_confidence,
                    source = excluded.source;
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
            command.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
