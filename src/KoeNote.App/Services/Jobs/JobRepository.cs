using KoeNote.App.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRepository(AppPaths paths)
{
    public JobSummary CreateFromAudio(string sourceAudioPath)
    {
        var now = DateTimeOffset.Now;
        var jobId = now.ToString("yyyyMMdd-HHmmssfff");
        var title = Path.GetFileNameWithoutExtension(sourceAudioPath);
        var fileName = Path.GetFileName(sourceAudioPath);

        var job = new JobSummary(
            jobId,
            title,
            fileName,
            sourceAudioPath,
            "登録済み",
            0,
            0,
            now);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO jobs (
                job_id,
                title,
                source_audio_path,
                status,
                current_stage,
                progress_percent,
                created_at,
                updated_at,
                asr_engine,
                asr_model,
                review_model
            )
            VALUES (
                $job_id,
                $title,
                $source_audio_path,
                $status,
                $current_stage,
                $progress_percent,
                $created_at,
                $updated_at,
                $asr_engine,
                $asr_model,
                $review_model
            );
            """;
        command.Parameters.AddWithValue("$job_id", job.JobId);
        command.Parameters.AddWithValue("$title", job.Title);
        command.Parameters.AddWithValue("$source_audio_path", job.SourceAudioPath);
        command.Parameters.AddWithValue("$status", job.Status);
        command.Parameters.AddWithValue("$current_stage", "created");
        command.Parameters.AddWithValue("$progress_percent", job.ProgressPercent);
        command.Parameters.AddWithValue("$created_at", now.ToString("o"));
        command.Parameters.AddWithValue("$updated_at", now.ToString("o"));
        command.Parameters.AddWithValue("$asr_engine", "vibevoice-asr-gguf");
        command.Parameters.AddWithValue("$asr_model", "vibevoice-asr-q4_k.gguf");
        command.Parameters.AddWithValue("$review_model", "llm-jp-4-8B-thinking-Q4_K_M.gguf");
        command.ExecuteNonQuery();

        return job;
    }

    public void UpdatePreprocessResult(
        JobSummary job,
        string status,
        string currentStage,
        int progressPercent,
        string? normalizedAudioPath,
        string? errorCategory = null)
    {
        job.Status = status;
        job.ProgressPercent = progressPercent;
        job.NormalizedAudioPath = normalizedAudioPath;
        job.UpdatedAt = DateTimeOffset.Now;

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = $status,
                current_stage = $current_stage,
                progress_percent = $progress_percent,
                normalized_audio_path = $normalized_audio_path,
                updated_at = $updated_at,
                last_error_category = $last_error_category
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$current_stage", currentStage);
        command.Parameters.AddWithValue("$progress_percent", progressPercent);
        command.Parameters.AddWithValue("$normalized_audio_path", (object?)normalizedAudioPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$updated_at", job.UpdatedAt.ToString("o"));
        command.Parameters.AddWithValue("$last_error_category", (object?)errorCategory ?? DBNull.Value);
        command.Parameters.AddWithValue("$job_id", job.JobId);
        command.ExecuteNonQuery();
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
}
