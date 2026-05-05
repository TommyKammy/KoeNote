using KoeNote.App.Models;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Asr;

public sealed class TranscriptSegmentRepository(AppPaths paths)
{
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

        transaction.Commit();
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
