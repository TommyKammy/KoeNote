using KoeNote.App.Services;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptReadRepository(AppPaths paths)
{
    public IReadOnlyList<TranscriptReadModel> ReadForJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return [];
        }

        using var connection = SqliteConnectionFactory.Open(paths);
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
        var segments = new List<TranscriptReadModel>();
        while (reader.Read())
        {
            segments.Add(new TranscriptReadModel(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9)));
        }

        return segments;
    }
}
