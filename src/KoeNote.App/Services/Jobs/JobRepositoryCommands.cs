using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Jobs;

internal static class JobRepositoryCommands
{
    public static SqliteCommand CreateLoadJobsCommand(
        SqliteConnection connection,
        bool isDeleted,
        int limit)
    {
        return connection.CreateCommand("""
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
            """)
            .AddValue("$is_deleted", isDeleted ? 1 : 0)
            .AddValue("$limit", limit);
    }

    public static SqliteCommand CreateRestoreJobCommand(
        SqliteConnection connection,
        string jobId,
        DateTimeOffset timestamp)
    {
        return connection.CreateCommand("""
            UPDATE jobs
            SET is_deleted = 0,
                deleted_at = NULL,
                delete_reason = '',
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """)
            .AddValue("$updated_at", timestamp.ToString("o"))
            .AddValue("$job_id", jobId);
    }

    public static SqliteCommand CreateSoftDeleteJobCommand(
        SqliteConnection connection,
        string jobId,
        string reason,
        DateTimeOffset timestamp)
    {
        var deletedAt = timestamp.ToString("o");
        return connection.CreateCommand("""
            UPDATE jobs
            SET is_deleted = 1,
                deleted_at = $deleted_at,
                delete_reason = $delete_reason,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """)
            .AddValue("$deleted_at", deletedAt)
            .AddValue("$delete_reason", reason)
            .AddValue("$updated_at", deletedAt)
            .AddValue("$job_id", jobId);
    }

    public static SqliteCommand CreateIsDeletedJobCommand(SqliteConnection connection, string jobId)
    {
        return connection.CreateCommand("""
            SELECT 1
            FROM jobs
            WHERE job_id = $job_id AND is_deleted = 1;
            """)
            .AddValue("$job_id", jobId);
    }

    public static SqliteCommand CreateIsUnstartedRegisteredJobCommand(SqliteConnection connection, string jobId)
    {
        return connection.CreateCommand("""
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
            """)
            .AddValue("$job_id", jobId);
    }

    public static IEnumerable<string> PermanentDeleteStatementsForJobId()
    {
        return PermanentDeleteStatements("WHERE job_id = $job_id");
    }

    private static IEnumerable<string> PermanentDeleteStatements(string whereClause)
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
}
