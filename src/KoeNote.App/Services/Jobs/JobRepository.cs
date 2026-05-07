using KoeNote.App.Models;
using System.IO;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRepository(AppPaths paths)
{
    public IReadOnlyList<JobSummary> LoadRecent(int limit = 50)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        NormalizeReviewCompletedJobs(connection);
        return LoadJobs(connection, paths, isDeleted: false, limit);
    }

    public IReadOnlyList<JobSummary> LoadDeleted(int limit = 200)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        return LoadJobs(connection, paths, isDeleted: true, limit);
    }

    private static IReadOnlyList<JobSummary> LoadJobs(Microsoft.Data.Sqlite.SqliteConnection connection, AppPaths paths, bool isDeleted, int limit)
    {
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
                updated_at,
                is_deleted,
                deleted_at,
                delete_reason
            FROM jobs
            WHERE is_deleted = $is_deleted
            ORDER BY updated_at DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$is_deleted", isDeleted ? 1 : 0);
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
                NormalizeDisplayStatus(reader.GetString(4)),
                reader.GetInt32(5),
                reader.GetInt32(6),
                updatedAt,
                createdAt,
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(9) != 0,
                reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                isDeleted ? CalculateDirectorySize(Path.Combine(paths.Jobs, reader.GetString(0))) : 0));
        }

        return jobs;
    }

    public long GetJobStorageBytes(string jobId)
    {
        return CalculateDirectorySize(Path.Combine(paths.Jobs, jobId));
    }

    private static void NormalizeReviewCompletedJobs(Microsoft.Data.Sqlite.SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            WITH pending(job_id, value) AS (
                SELECT job_id, COUNT(*)
                FROM correction_drafts
                WHERE status = 'pending'
                GROUP BY job_id
            )
            UPDATE jobs
            SET status = '整文完了',
                current_stage = 'review_completed',
                progress_percent = 100,
                unreviewed_draft_count = 0,
                updated_at = $updated_at
            WHERE COALESCE((SELECT value FROM pending WHERE pending.job_id = jobs.job_id), 0) = 0
                AND is_deleted = 0
                AND unreviewed_draft_count = 0
                AND (
                    current_stage = 'review_ready'
                    OR status = '整文候補なし'
                    OR status = '推敲候補なし'
                );
            """;
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static string NormalizeDisplayStatus(string status)
    {
        return status switch
        {
            "レビュー待ち" => "整文待ち",
            "レビュー完了" => "整文完了",
            "レビュー済み" => "整文済み",
            "レビュー中" => "整文中",
            "レビュー失敗" => "整文失敗",
            "推敲候補なし" => "整文候補なし",
            "推敲中" => "整文中",
            _ => status
        };
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
        command.Parameters.AddWithValue("$asr_engine", "faster-whisper");
        command.Parameters.AddWithValue("$asr_model", "faster-whisper-large-v3-turbo");
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
        string? errorCategory = null,
        int? unreviewedDraftCount = null)
    {
        job.Status = status;
        job.ProgressPercent = progressPercent;
        job.NormalizedAudioPath = normalizedAudioPath;
        if (unreviewedDraftCount is not null)
        {
            job.UnreviewedDrafts = unreviewedDraftCount.Value;
        }

        job.UpdatedAt = DateTimeOffset.Now;

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET status = $status,
                current_stage = $current_stage,
                progress_percent = $progress_percent,
                normalized_audio_path = $normalized_audio_path,
                unreviewed_draft_count = COALESCE($unreviewed_draft_count, unreviewed_draft_count),
                updated_at = $updated_at,
                last_error_category = $last_error_category
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$current_stage", currentStage);
        command.Parameters.AddWithValue("$progress_percent", progressPercent);
        command.Parameters.AddWithValue("$normalized_audio_path", (object?)normalizedAudioPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$unreviewed_draft_count", (object?)unreviewedDraftCount ?? DBNull.Value);
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
        UpdatePreprocessResult(job, "整文中", "reviewing", 82, job.NormalizedAudioPath);
    }

    public void MarkReviewSucceeded(JobSummary job, int draftCount)
    {
        UpdatePreprocessResult(
            job,
            draftCount > 0 ? "整文待ち" : "整文完了",
            draftCount > 0 ? "review_ready" : "review_completed",
            draftCount > 0 ? 90 : 100,
            job.NormalizedAudioPath,
            unreviewedDraftCount: draftCount);
    }

    public void MarkReviewSkippedAndClearDrafts(JobSummary job)
    {
        job.Status = "整文完了";
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
        UpdatePreprocessResult(job, $"整文失敗: {errorCategory}", "review_failed", 90, job.NormalizedAudioPath, errorCategory);
    }

    public void MarkSummaryRunning(JobSummary job)
    {
        UpdatePreprocessResult(job, "要約中", "summarizing", 94, job.NormalizedAudioPath);
    }

    public void MarkSummarySucceeded(JobSummary job)
    {
        UpdatePreprocessResult(job, "要約完了", "summary_completed", 100, job.NormalizedAudioPath);
    }

    public void MarkSummarySkipped(JobSummary job)
    {
        UpdatePreprocessResult(job, "要約スキップ", "summary_skipped", 100, job.NormalizedAudioPath);
    }

    public void MarkSummaryFailed(JobSummary job, string errorCategory)
    {
        UpdatePreprocessResult(job, $"要約失敗: {errorCategory}", "summary_failed", 96, job.NormalizedAudioPath, errorCategory);
    }

    public void MarkCancelled(JobSummary job, string currentStage)
    {
        UpdatePreprocessResult(job, "キャンセル済み", $"{currentStage}_cancelled", job.ProgressPercent, job.NormalizedAudioPath, "cancelled");
    }

    public bool DeleteJob(string jobId)
    {
        return DeleteJob(jobId, "manual");
    }

    private bool DeleteJob(string jobId, string reason)
    {
        if (IsUnstartedRegisteredJob(jobId))
        {
            PermanentlyDeleteJobs([jobId]);
            return false;
        }

        SoftDeleteJob(jobId, reason);
        return true;
    }

    public IReadOnlySet<string> DeleteAllJobs()
    {
        var movedToHistory = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in LoadRecent(int.MaxValue))
        {
            if (DeleteJob(job.JobId, "clear_all"))
            {
                movedToHistory.Add(job.JobId);
            }
        }

        return movedToHistory;
    }

    public void RestoreJob(string jobId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET is_deleted = 0,
                deleted_at = NULL,
                delete_reason = '',
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.Parameters.AddWithValue("$job_id", jobId);
        command.ExecuteNonQuery();
    }

    public void PermanentlyDeleteJob(string jobId)
    {
        if (!IsDeletedJob(jobId))
        {
            return;
        }

        PermanentlyDeleteJobs([jobId]);
    }

    public void PermanentlyDeleteAllDeletedJobs()
    {
        var jobIds = LoadDeleted(int.MaxValue).Select(static job => job.JobId).ToArray();
        PermanentlyDeleteJobs(jobIds);
    }

    private void SoftDeleteJob(string jobId, string reason)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs
            SET is_deleted = 1,
                deleted_at = $deleted_at,
                delete_reason = $delete_reason,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        var now = DateTimeOffset.Now.ToString("o");
        command.Parameters.AddWithValue("$deleted_at", now);
        command.Parameters.AddWithValue("$delete_reason", reason);
        command.Parameters.AddWithValue("$updated_at", now);
        command.Parameters.AddWithValue("$job_id", jobId);
        command.ExecuteNonQuery();
    }

    private bool IsDeletedJob(string jobId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM jobs
            WHERE job_id = $job_id AND is_deleted = 1;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        return command.ExecuteScalar() is not null;
    }

    private bool IsUnstartedRegisteredJob(string jobId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT 1
            FROM jobs
            WHERE job_id = $job_id
                AND is_deleted = 0
                AND current_stage = 'created'
                AND progress_percent = 0
                AND normalized_audio_path IS NULL
                AND NOT EXISTS (SELECT 1 FROM transcript_segments WHERE transcript_segments.job_id = jobs.job_id)
                AND NOT EXISTS (SELECT 1 FROM correction_drafts WHERE correction_drafts.job_id = jobs.job_id)
                AND NOT EXISTS (SELECT 1 FROM stage_progress WHERE stage_progress.job_id = jobs.job_id)
                AND NOT EXISTS (SELECT 1 FROM asr_runs WHERE asr_runs.job_id = jobs.job_id);
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        return command.ExecuteScalar() is not null;
    }

    private void PermanentlyDeleteJobs(IReadOnlyCollection<string> jobIds)
    {
        if (jobIds.Count == 0)
        {
            return;
        }

        using var connection = SqliteConnectionFactory.Open(paths);
        using var transaction = connection.BeginTransaction();

        foreach (var jobId in jobIds)
        {
            foreach (var sql in DeleteStatements("WHERE job_id = $job_id"))
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = sql;
                command.Parameters.AddWithValue("$job_id", jobId);
                command.ExecuteNonQuery();
            }
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
        yield return $"DELETE FROM asr_runs{suffix};";
        yield return $"DELETE FROM jobs{suffix};";
    }

    private static long CalculateDirectorySize(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Sum(static file =>
                {
                    try
                    {
                        return new FileInfo(file).Length;
                    }
                    catch (IOException)
                    {
                        return 0;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return 0;
                    }
                });
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
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
