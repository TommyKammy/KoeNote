namespace KoeNote.App.Services.Asr;

public sealed class AsrRunRepository(AppPaths paths)
{
    public string Start(
        string jobId,
        string engineId,
        string modelId,
        string? modelVersion = null)
    {
        var asrRunId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.Now.ToString("o");

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO asr_runs (
                asr_run_id,
                job_id,
                engine_id,
                model_id,
                model_version,
                status,
                started_at,
                created_at
            )
            VALUES (
                $asr_run_id,
                $job_id,
                $engine_id,
                $model_id,
                $model_version,
                'running',
                $started_at,
                $created_at
            );
            """;
        command.Parameters.AddWithValue("$asr_run_id", asrRunId);
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$engine_id", engineId);
        command.Parameters.AddWithValue("$model_id", modelId);
        command.Parameters.AddWithValue("$model_version", (object?)modelVersion ?? DBNull.Value);
        command.Parameters.AddWithValue("$started_at", now);
        command.Parameters.AddWithValue("$created_at", now);
        command.ExecuteNonQuery();

        return asrRunId;
    }

    public void MarkSucceeded(
        string asrRunId,
        TimeSpan duration,
        string rawOutputPath,
        string normalizedOutputPath)
    {
        Finish(asrRunId, "succeeded", duration, rawOutputPath, normalizedOutputPath, errorCategory: null);
    }

    public void MarkFailed(string asrRunId, string errorCategory)
    {
        Finish(asrRunId, "failed", duration: null, rawOutputPath: null, normalizedOutputPath: null, errorCategory);
    }

    public void MarkCancelled(string asrRunId)
    {
        Finish(asrRunId, "cancelled", duration: null, rawOutputPath: null, normalizedOutputPath: null, errorCategory: "cancelled");
    }

    private void Finish(
        string asrRunId,
        string status,
        TimeSpan? duration,
        string? rawOutputPath,
        string? normalizedOutputPath,
        string? errorCategory)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE asr_runs
            SET
                status = $status,
                finished_at = $finished_at,
                duration_seconds = $duration_seconds,
                raw_output_path = $raw_output_path,
                normalized_output_path = $normalized_output_path,
                error_category = $error_category
            WHERE asr_run_id = $asr_run_id;
            """;
        command.Parameters.AddWithValue("$asr_run_id", asrRunId);
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$finished_at", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$duration_seconds", (object?)duration?.TotalSeconds ?? DBNull.Value);
        command.Parameters.AddWithValue("$raw_output_path", (object?)rawOutputPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$normalized_output_path", (object?)normalizedOutputPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$error_category", (object?)errorCategory ?? DBNull.Value);
        command.ExecuteNonQuery();
    }
}
