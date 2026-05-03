using KoeNote.App.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRepository(AppPaths paths)
{
    public IReadOnlyList<JobSummary> LoadRecent(int limit = 50)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                job_id,
                title,
                source_audio_path,
                normalized_audio_path,
                status,
                progress_percent,
                unreviewed_draft_count,
                created_at,
                updated_at
            FROM jobs
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var jobs = new List<JobSummary>();
        while (reader.Read())
        {
            var sourceAudioPath = reader.GetString(2);
            var createdAt = DateTimeOffset.Parse(reader.GetString(7));
            var updatedAt = DateTimeOffset.Parse(reader.GetString(8));
            jobs.Add(new JobSummary(
                reader.GetString(0),
                reader.GetString(1),
                Path.GetFileName(sourceAudioPath),
                sourceAudioPath,
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                updatedAt,
                createdAt,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }

        return jobs;
    }

    public JobSummary CreateFromAudio(string sourceAudioPath)
    {
        var now = DateTimeOffset.Now;
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        var jobId = $"{now:yyyyMMdd-HHmmssfff}-{uniqueSuffix}";
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

    public void MarkPreprocessRunning(JobSummary job)
    {
        UpdatePreprocessResult(job, "音声変換中", "preprocessing", 10, null);
    }

    public void MarkPreprocessSucceeded(JobSummary job, string normalizedAudioPath)
    {
        UpdatePreprocessResult(job, "音声変換完了", "preprocessed", 100, normalizedAudioPath);
    }

    public void MarkPreprocessFailed(JobSummary job, string errorCategory)
    {
        UpdatePreprocessResult(job, "音声変換失敗", "preprocessing_failed", 100, null, errorCategory);
    }

    public void MarkAsrRunning(JobSummary job)
    {
        UpdatePreprocessResult(job, "文字起こし中", "asr", 45, job.NormalizedAudioPath);
    }

    public void MarkAsrSucceeded(JobSummary job)
    {
        UpdatePreprocessResult(job, "文字起こし完了", "asr_completed", 70, job.NormalizedAudioPath);
    }

    public void MarkAsrFailed(JobSummary job, string errorCategory)
    {
        UpdatePreprocessResult(job, $"ASR失敗: {errorCategory}", "asr_failed", 70, job.NormalizedAudioPath, errorCategory);
    }

    public void MarkReviewRunning(JobSummary job)
    {
        UpdatePreprocessResult(job, "推敲中", "reviewing", 82, job.NormalizedAudioPath);
    }

    public void MarkReviewSucceeded(JobSummary job, int draftCount)
    {
        UpdatePreprocessResult(job, draftCount > 0 ? "レビュー待ち" : "推敲候補なし", "review_ready", 90, job.NormalizedAudioPath);
    }

    public void MarkReviewFailed(JobSummary job, string errorCategory)
    {
        UpdatePreprocessResult(job, $"推敲失敗: {errorCategory}", "review_failed", 90, job.NormalizedAudioPath, errorCategory);
    }

    public void MarkCancelled(JobSummary job, string currentStage)
    {
        UpdatePreprocessResult(job, "キャンセル済み", $"{currentStage}_cancelled", job.ProgressPercent, job.NormalizedAudioPath, "cancelled");
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
