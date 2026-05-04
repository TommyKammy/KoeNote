using KoeNote.App.Models;
using System.IO;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRepository(AppPaths paths)
{
    public IReadOnlyList<JobSummary> LoadRecent(int limit = 50)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
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

        using var connection = SqliteConnectionFactory.Open(paths);
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

        using var connection = SqliteConnectionFactory.Open(paths);
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

    public void MarkReviewSkippedAndClearDrafts(JobSummary job)
    {
        job.Status = "Review skipped";
        job.ProgressPercent = 100;
        job.UnreviewedDrafts = 0;
        job.UpdatedAt = DateTimeOffset.Now;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        using (var jobCommand = connection.CreateCommand())
        {
            jobCommand.Transaction = transaction;
            jobCommand.CommandText = """
                UPDATE jobs
                SET status = $status,
                    current_stage = 'review_skipped',
                    progress_percent = 100,
                    normalized_audio_path = $normalized_audio_path,
                    unreviewed_draft_count = 0,
                    updated_at = $updated_at,
                    last_error_category = NULL
                WHERE job_id = $job_id;
                """;
            jobCommand.Parameters.AddWithValue("$status", job.Status);
            jobCommand.Parameters.AddWithValue("$normalized_audio_path", (object?)job.NormalizedAudioPath ?? DBNull.Value);
            jobCommand.Parameters.AddWithValue("$updated_at", job.UpdatedAt.ToString("o"));
            jobCommand.Parameters.AddWithValue("$job_id", job.JobId);
            jobCommand.ExecuteNonQuery();
        }

        using (var draftCommand = connection.CreateCommand())
        {
            draftCommand.Transaction = transaction;
            draftCommand.CommandText = """
                UPDATE correction_drafts
                SET status = 'skipped'
                WHERE job_id = $job_id AND status = 'pending';
                """;
            draftCommand.Parameters.AddWithValue("$job_id", job.JobId);
            draftCommand.ExecuteNonQuery();
        }

        using (var segmentCommand = connection.CreateCommand())
        {
            segmentCommand.Transaction = transaction;
            segmentCommand.CommandText = """
                UPDATE transcript_segments
                SET review_state = 'none'
                WHERE job_id = $job_id AND review_state = 'has_draft';
                """;
            segmentCommand.Parameters.AddWithValue("$job_id", job.JobId);
            segmentCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    public void MarkReviewFailed(JobSummary job, string errorCategory)
    {
        UpdatePreprocessResult(job, $"推敲失敗: {errorCategory}", "review_failed", 90, job.NormalizedAudioPath, errorCategory);
    }

    public void MarkCancelled(JobSummary job, string currentStage)
    {
        UpdatePreprocessResult(job, "キャンセル済み", $"{currentStage}_cancelled", job.ProgressPercent, job.NormalizedAudioPath, "cancelled");
    }

    public void DeleteJob(string jobId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        foreach (var sql in DeleteStatements("WHERE job_id = $job_id"))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$job_id", jobId);
            command.ExecuteNonQuery();
        }

        transaction.Commit();
        DeleteJobDirectory(jobId);
    }

    public void DeleteAllJobs()
    {
        var jobIds = LoadRecent(int.MaxValue).Select(static job => job.JobId).ToArray();

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        foreach (var sql in DeleteStatements(string.Empty))
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        transaction.Commit();

        foreach (var jobId in jobIds)
        {
            DeleteJobDirectory(jobId);
        }
    }

    private static IEnumerable<string> DeleteStatements(string whereClause)
    {
        var suffix = string.IsNullOrWhiteSpace(whereClause) ? string.Empty : $" {whereClause}";
        yield return $"DELETE FROM correction_memory_events{suffix};";
        yield return $"DELETE FROM review_operation_history{suffix};";
        yield return $"DELETE FROM review_decisions WHERE draft_id IN (SELECT draft_id FROM correction_drafts{suffix});";
        yield return $"DELETE FROM correction_drafts{suffix};";
        yield return $"DELETE FROM speaker_aliases{suffix};";
        yield return $"DELETE FROM transcript_segments{suffix};";
        yield return $"DELETE FROM stage_progress{suffix};";
        yield return $"DELETE FROM job_log_events{suffix};";
        yield return $"DELETE FROM jobs{suffix};";
    }

    private void DeleteJobDirectory(string jobId)
    {
        var jobDirectory = Path.Combine(paths.Jobs, jobId);
        try
        {
            if (Directory.Exists(jobDirectory))
            {
                Directory.Delete(jobDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
