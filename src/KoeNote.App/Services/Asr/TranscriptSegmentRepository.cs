using KoeNote.App.Models;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Asr;

public sealed class TranscriptSegmentRepository(AppPaths paths)
{
    public IReadOnlyList<TranscriptSegmentPreview> ReadPreviews(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.segment_id,
                s.start_seconds,
                s.end_seconds,
                COALESCE(a.display_name, s.speaker_name, s.speaker_id, ''),
                COALESCE(s.final_text, s.normalized_text, s.raw_text),
                s.review_state,
                COALESCE(s.speaker_id, ''),
                s.raw_text,
                s.normalized_text,
                s.final_text
            FROM transcript_segments s
            LEFT JOIN speaker_aliases a
                ON a.job_id = s.job_id AND a.speaker_id = s.speaker_id
            WHERE s.job_id = $job_id
            ORDER BY s.start_seconds ASC, s.end_seconds ASC;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);

        using var reader = command.ExecuteReader();
        var previews = new List<TranscriptSegmentPreview>();
        while (reader.Read())
        {
            previews.Add(new TranscriptSegmentPreview(
                FormatTimestamp(reader.GetDouble(1)),
                FormatTimestamp(reader.GetDouble(2)),
                reader.GetString(3),
                reader.GetString(4),
                FormatReviewState(reader.GetString(5)),
                reader.GetString(0),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return previews;
    }

    public void SaveSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0)
        {
            return;
        }

        using var connection = OpenConnection();
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

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private static string FormatTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private static string FormatReviewState(string state)
    {
        return state switch
        {
            "has_draft" => "推敲候補あり",
            "reviewed" => "レビュー済み",
            "manually_edited" => "手修正済み",
            _ => "候補なし"
        };
    }
}
