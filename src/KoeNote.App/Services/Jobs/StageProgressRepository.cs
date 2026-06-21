using KoeNote.App.Services;

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
        using var command = connection.CreateCommand("""
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
            """);
        command
            .AddValue("$stage_id", $"{jobId}-{stage}")
            .AddValue("$job_id", jobId)
            .AddValue("$stage", stage)
            .AddValue("$status", status)
            .AddValue("$progress_percent", progressPercent)
            .AddIsoDateTimeOffset("$started_at", startedAt)
            .AddIsoDateTimeOffset("$finished_at", finishedAt)
            .AddValue("$duration_seconds", durationSeconds)
            .AddValue("$exit_code", exitCode)
            .AddValue("$error_category", errorCategory)
            .AddValue("$log_path", logPath);
        command.ExecuteNonQuery();
    }

}
