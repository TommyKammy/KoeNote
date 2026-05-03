using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public sealed class DatabaseInitializer(AppPaths paths)
{
    public void EnsureCreated()
    {
        using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL;");
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS jobs (
                job_id TEXT NOT NULL PRIMARY KEY,
                title TEXT NOT NULL,
                source_audio_path TEXT NOT NULL,
                normalized_audio_path TEXT,
                status TEXT NOT NULL,
                current_stage TEXT,
                progress_percent INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                asr_engine TEXT,
                asr_model TEXT,
                review_model TEXT,
                unreviewed_draft_count INTEGER NOT NULL DEFAULT 0,
                last_error_category TEXT
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS transcript_segments (
                segment_id TEXT NOT NULL,
                job_id TEXT NOT NULL,
                start_seconds REAL NOT NULL,
                end_seconds REAL NOT NULL,
                speaker_id TEXT,
                speaker_name TEXT,
                raw_text TEXT NOT NULL,
                normalized_text TEXT,
                final_text TEXT,
                review_state TEXT NOT NULL DEFAULT 'none',
                asr_confidence REAL,
                source TEXT NOT NULL DEFAULT 'asr',
                PRIMARY KEY (job_id, segment_id)
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS correction_drafts (
                draft_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                segment_id TEXT NOT NULL,
                issue_type TEXT NOT NULL,
                original_text TEXT NOT NULL,
                suggested_text TEXT NOT NULL,
                reason TEXT NOT NULL,
                confidence REAL NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS review_decisions (
                decision_id TEXT NOT NULL PRIMARY KEY,
                draft_id TEXT NOT NULL,
                action TEXT NOT NULL,
                final_text TEXT,
                manual_note TEXT,
                decided_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS stage_progress (
                stage_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT NOT NULL,
                stage TEXT NOT NULL,
                status TEXT NOT NULL,
                progress_percent INTEGER NOT NULL DEFAULT 0,
                started_at TEXT,
                finished_at TEXT,
                duration_seconds REAL,
                exit_code INTEGER,
                error_category TEXT,
                log_path TEXT
            );
            """);
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS job_log_events (
                log_event_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT,
                stage TEXT,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            INSERT OR IGNORE INTO schema_version (version, applied_at)
            VALUES (1, datetime('now'));
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
