namespace KoeNote.App.Services.Jobs;

public sealed class StageProgressRepository(AppPaths paths)
{
    public void Upsert(
        string jobId,
        string stage,
        string status,
        int progressPercent,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? finishedAt = null,
        double? durationSeconds = null,
        int? exitCode = null,
        string? errorCategory = null,
        string? logPath = null)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO stage_progress (
                stage_id,
                job_id,
                stage,
                status,
                progress_percent,
                started_at,
                finished_at,
                duration_seconds,
                exit_code,
                error_category,
                log_path
            )
            VALUES (
                $stage_id,
                $job_id,
                $stage,
                $status,
                $progress_percent,
                $started_at,
                $finished_at,
                $duration_seconds,
                $exit_code,
                $error_category,
                $log_path
            )
            ON CONFLICT(stage_id) DO UPDATE SET
                status = excluded.status,
                progress_percent = excluded.progress_percent,
                finished_at = excluded.finished_at,
                duration_seconds = excluded.duration_seconds,
                exit_code = excluded.exit_code,
                error_category = excluded.error_category,
                log_path = excluded.log_path;
            """;
        command.Parameters.AddWithValue("$stage_id", $"{jobId}-{stage}");
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$progress_percent", progressPercent);
        command.Parameters.AddWithValue("$started_at", (object?)startedAt?.ToString("o") ?? DBNull.Value);
        command.Parameters.AddWithValue("$finished_at", (object?)finishedAt?.ToString("o") ?? DBNull.Value);
        command.Parameters.AddWithValue("$duration_seconds", (object?)durationSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$exit_code", (object?)exitCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_category", (object?)errorCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("$log_path", (object?)logPath ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

}
