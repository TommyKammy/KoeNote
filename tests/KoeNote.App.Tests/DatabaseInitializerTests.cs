using Microsoft.Data.Sqlite;
using KoeNote.App.Services;

namespace KoeNote.App.Tests;

public sealed class DatabaseInitializerTests
{
    [Fact]
    public void EnsureCreated_CreatesExpectedDatabaseTables()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();

        var initializer = new DatabaseInitializer(paths);
        initializer.EnsureCreated();

        Assert.True(File.Exists(paths.DatabasePath));

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        var tables = ReadTableNames(connection);

        Assert.Contains("jobs", tables);
        Assert.Contains("transcript_segments", tables);
        Assert.Contains("correction_drafts", tables);
        Assert.Contains("review_decisions", tables);
        Assert.Contains("stage_progress", tables);
        Assert.Contains("job_log_events", tables);
        Assert.Contains("asr_settings", tables);
        Assert.Contains("speaker_aliases", tables);
        Assert.Contains("review_operation_history", tables);
        Assert.Contains("user_terms", tables);
        Assert.Contains("correction_memory", tables);
        Assert.Contains("correction_memory_events", tables);
        Assert.Contains("asr_runs", tables);
        Assert.Contains("installed_models", tables);
        Assert.Contains("installed_runtimes", tables);
        Assert.Contains("model_download_jobs", tables);
        Assert.Contains("review_guidelines", tables);
        Assert.Contains("domain_preset_imports", tables);
        Assert.Contains("domain_preset_speaker_alias_imports", tables);
        Assert.Contains("transcript_derivatives", tables);
        Assert.Contains("transcript_derivative_chunks", tables);
        Assert.Contains("llm_profiles", tables);
        Assert.Contains("llm_task_settings", tables);

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], ReadSchemaVersions(connection));
        Assert.Contains("last_error_category", ReadColumnNames(connection, "jobs"));
        Assert.Contains("is_deleted", ReadColumnNames(connection, "jobs"));
        Assert.Contains("deleted_at", ReadColumnNames(connection, "jobs"));
        Assert.Contains("delete_reason", ReadColumnNames(connection, "jobs"));
        Assert.Contains("engine_id", ReadColumnNames(connection, "asr_settings"));
        Assert.Contains("enable_review_stage", ReadColumnNames(connection, "asr_settings"));
        Assert.Contains("source", ReadColumnNames(connection, "correction_drafts"));
        Assert.Contains("source_ref_id", ReadColumnNames(connection, "correction_drafts"));
        Assert.Contains("asr_run_id", ReadColumnNames(connection, "transcript_segments"));
        Assert.Contains("idx_job_log_events_job_created", ReadIndexNames(connection));
        Assert.Contains("idx_correction_drafts_job_status", ReadIndexNames(connection));
        Assert.Contains("idx_review_operation_history_job_created", ReadIndexNames(connection));
        Assert.Contains("idx_review_operation_history_created", ReadIndexNames(connection));
        Assert.Contains("idx_jobs_deleted_updated", ReadIndexNames(connection));
        Assert.Contains("idx_review_guidelines_preset_text", ReadIndexNames(connection));
        Assert.Contains("idx_domain_preset_imports_imported", ReadIndexNames(connection));
        Assert.Contains("idx_domain_preset_speaker_alias_imports_import", ReadIndexNames(connection));
        Assert.Contains("idx_transcript_derivatives_job_kind_updated", ReadIndexNames(connection));
        Assert.Contains("idx_transcript_derivative_chunks_derivative_index", ReadIndexNames(connection));
        Assert.Contains("idx_llm_profiles_model", ReadIndexNames(connection));
        Assert.Contains("idx_llm_profiles_active", ReadIndexNames(connection));
        Assert.Contains("idx_llm_task_settings_profile", ReadIndexNames(connection));
        Assert.Contains("deactivated_at", ReadColumnNames(connection, "domain_preset_imports"));
        Assert.Contains("previous_display_name", ReadColumnNames(connection, "domain_preset_speaker_alias_imports"));
        Assert.Contains("applied_display_name", ReadColumnNames(connection, "domain_preset_speaker_alias_imports"));
    }

    [Fact]
    public void EnsureCreated_AppliesPendingMigrationToVersionOneDatabase()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        CreateVersionOneDatabase(paths);

        new DatabaseInitializer(paths).EnsureCreated();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], ReadSchemaVersions(connection));
        Assert.Contains("last_error_category", ReadColumnNames(connection, "jobs"));
        Assert.Contains("asr_settings", ReadTableNames(connection));
        Assert.Contains("engine_id", ReadColumnNames(connection, "asr_settings"));
        Assert.Contains("enable_review_stage", ReadColumnNames(connection, "asr_settings"));
        Assert.Contains("speaker_aliases", ReadTableNames(connection));
        Assert.Contains("review_operation_history", ReadTableNames(connection));
        Assert.Contains("user_terms", ReadTableNames(connection));
        Assert.Contains("correction_memory", ReadTableNames(connection));
        Assert.Contains("correction_memory_events", ReadTableNames(connection));
        Assert.Contains("source", ReadColumnNames(connection, "correction_drafts"));
        Assert.Contains("asr_runs", ReadTableNames(connection));
        Assert.Contains("asr_run_id", ReadColumnNames(connection, "transcript_segments"));
        Assert.Contains("installed_models", ReadTableNames(connection));
        Assert.Contains("installed_runtimes", ReadTableNames(connection));
        Assert.Contains("model_download_jobs", ReadTableNames(connection));
        Assert.Contains("is_deleted", ReadColumnNames(connection, "jobs"));
        Assert.Contains("review_guidelines", ReadTableNames(connection));
        Assert.Contains("domain_preset_imports", ReadTableNames(connection));
        Assert.Contains("domain_preset_speaker_alias_imports", ReadTableNames(connection));
        Assert.Contains("transcript_derivatives", ReadTableNames(connection));
        Assert.Contains("transcript_derivative_chunks", ReadTableNames(connection));
        Assert.Contains("llm_profiles", ReadTableNames(connection));
        Assert.Contains("llm_task_settings", ReadTableNames(connection));
        Assert.Contains("previous_display_name", ReadColumnNames(connection, "domain_preset_speaker_alias_imports"));
        Assert.Contains("applied_display_name", ReadColumnNames(connection, "domain_preset_speaker_alias_imports"));
    }

    [Fact]
    public void EnsureCreated_CreatesUpdateBackupBeforeMigratingExistingDatabase()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        CreateVersionOneDatabase(paths);
        File.WriteAllText(paths.SettingsPath, "{ \"asrEngine\": \"legacy\" }");
        File.WriteAllText(Path.Combine(paths.Jobs, "legacy-job.txt"), "legacy job");

        new DatabaseInitializer(paths).EnsureCreated();

        var backupDirectory = Assert.Single(Directory.EnumerateDirectories(paths.UpdateBackups));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "jobs.sqlite")));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "settings.json")));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "jobs", "legacy-job.txt")));
        Assert.True(File.Exists(Path.Combine(backupDirectory, "backup-manifest.json")));
    }

    [Fact]
    public void EnsureCreated_DoesNotMutateExistingDatabaseBeforeBackup()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        CreateSchemaLessDatabase(paths);

        new DatabaseInitializer(paths).EnsureCreated();

        var backupDirectory = Assert.Single(Directory.EnumerateDirectories(paths.UpdateBackups));
        using var backupConnection = OpenDatabase(Path.Combine(backupDirectory, "jobs.sqlite"));
        var backupTables = ReadTableNames(backupConnection);

        Assert.Contains("legacy_table", backupTables);
        Assert.DoesNotContain("schema_version", backupTables);
    }

    [Fact]
    public void UpdateBackupService_CreatesUniqueBackupDirectoriesForRepeatedRuns()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        CreateVersionOneDatabase(paths);
        var service = new UpdateBackupService(paths);

        var first = service.CreateBeforeMigrationBackup(1, 14);
        var second = service.CreateBeforeMigrationBackup(1, 14);

        Assert.NotEqual(first.BackupDirectory, second.BackupDirectory);
        Assert.Equal(2, Directory.EnumerateDirectories(paths.UpdateBackups).Count());
    }

    [Fact]
    public void EnsureCreated_RejectsDatabaseCreatedByNewerAppVersion()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        CreateFutureVersionDatabase(paths);

        var ex = Assert.Throws<InvalidOperationException>(() => new DatabaseInitializer(paths).EnsureCreated());

        Assert.Contains("supports database schema up to", ex.Message);
        Assert.Empty(Directory.EnumerateDirectories(paths.UpdateBackups));
    }

    [Fact]
    public void EnsureCreated_IsIdempotent()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        var initializer = new DatabaseInitializer(paths);

        initializer.EnsureCreated();
        initializer.EnsureCreated();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], ReadSchemaVersions(connection));
    }

    [Fact]
    public void EnsureCreated_RepairsVersionSixDatabaseMissingAsrSettingsEngineId()
    {
        var root = CreateTempDirectory();
        var localRoot = CreateTempDirectory();
        var paths = new AppPaths(root, localRoot);
        paths.EnsureCreated();
        CreateVersionSixDatabaseMissingAsrSettingsEngineId(paths);

        new DatabaseInitializer(paths).EnsureCreated();

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17], ReadSchemaVersions(connection));
        Assert.Contains("engine_id", ReadColumnNames(connection, "asr_settings"));
        Assert.Contains("enable_review_stage", ReadColumnNames(connection, "asr_settings"));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static HashSet<string> ReadTableNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

        using var reader = command.ExecuteReader();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static SqliteConnection OpenDatabase(string databasePath)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private static List<int> ReadSchemaVersions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_version ORDER BY version;";

        using var reader = command.ExecuteReader();
        var versions = new List<int>();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private static HashSet<string> ReadColumnNames(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";

        using var reader = command.ExecuteReader();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static HashSet<string> ReadIndexNames(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'index';";

        using var reader = command.ExecuteReader();
        var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read())
        {
            indexes.Add(reader.GetString(0));
        }

        return indexes;
    }

    private static void CreateVersionOneDatabase(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            INSERT INTO schema_version (version, applied_at)
            VALUES (1, datetime('now'));
            """);
        Execute(connection, """
            CREATE TABLE jobs (
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
                unreviewed_draft_count INTEGER NOT NULL DEFAULT 0
            );
            """);
        Execute(connection, """
            CREATE TABLE transcript_segments (
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
            CREATE TABLE correction_drafts (
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
            CREATE TABLE review_decisions (
                decision_id TEXT NOT NULL PRIMARY KEY,
                draft_id TEXT NOT NULL,
                action TEXT NOT NULL,
                final_text TEXT,
                manual_note TEXT,
                decided_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            CREATE TABLE stage_progress (
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
            CREATE TABLE job_log_events (
                log_event_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT,
                stage TEXT,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }

    private static void CreateVersionSixDatabaseMissingAsrSettingsEngineId(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """);
        for (var version = 1; version <= 6; version++)
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO schema_version (version, applied_at)
                VALUES ($version, datetime('now'));
                """;
            command.Parameters.AddWithValue("$version", version);
            command.ExecuteNonQuery();
        }

        Execute(connection, """
            CREATE TABLE asr_settings (
                settings_id INTEGER NOT NULL PRIMARY KEY CHECK (settings_id = 1),
                context_text TEXT NOT NULL DEFAULT '',
                hotwords_text TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL
            );
            """);
    }

    private static void CreateFutureVersionDatabase(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """);
        Execute(connection, """
            INSERT INTO schema_version (version, applied_at)
            VALUES (999, datetime('now'));
            """);
    }

    private static void CreateSchemaLessDatabase(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();

        Execute(connection, """
            CREATE TABLE legacy_table (
                value TEXT NOT NULL
            );
            """);
        Execute(connection, """
            INSERT INTO legacy_table (value)
            VALUES ('before backup');
            """);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

