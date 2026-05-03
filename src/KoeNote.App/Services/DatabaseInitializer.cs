using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public sealed class DatabaseInitializer(AppPaths paths)
{
    private static readonly IReadOnlyList<DatabaseMigration> Migrations =
    [
        new(1, ApplyInitialSchema),
        new(2, ApplyStatusAndAsrSettingsSchema)
    ];

    public void EnsureCreated()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL;");
        EnsureSchemaVersionTable(connection);
        ApplyPendingMigrations(connection);
    }

    private static void ApplyPendingMigrations(SqliteConnection connection)
    {
        var appliedVersions = ReadAppliedVersions(connection);
        foreach (var migration in Migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            migration.Apply(connection, transaction);
            RecordAppliedVersion(connection, transaction, migration.Version);
            transaction.Commit();
        }
    }

    private static HashSet<int> ReadAppliedVersions(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT version FROM schema_version;";

        using var reader = command.ExecuteReader();
        var versions = new HashSet<int>();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    private static void EnsureSchemaVersionTable(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """);
    }

    private static void ApplyInitialSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        Execute(connection, transaction, """
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
                unreviewed_draft_count INTEGER NOT NULL DEFAULT 0
            );
            """);
        Execute(connection, transaction, """
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
        Execute(connection, transaction, """
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
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS review_decisions (
                decision_id TEXT NOT NULL PRIMARY KEY,
                draft_id TEXT NOT NULL,
                action TEXT NOT NULL,
                final_text TEXT,
                manual_note TEXT,
                decided_at TEXT NOT NULL
            );
            """);
        Execute(connection, transaction, """
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
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS job_log_events (
                log_event_id TEXT NOT NULL PRIMARY KEY,
                job_id TEXT,
                stage TEXT,
                level TEXT NOT NULL,
                message TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """);
    }

    private static void ApplyStatusAndAsrSettingsSchema(SqliteConnection connection, SqliteTransaction transaction)
    {
        AddColumnIfMissing(connection, transaction, "jobs", "last_error_category", "TEXT");
        Execute(connection, transaction, """
            CREATE TABLE IF NOT EXISTS asr_settings (
                settings_id INTEGER NOT NULL PRIMARY KEY CHECK (settings_id = 1),
                context_text TEXT NOT NULL DEFAULT '',
                hotwords_text TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL
            );
            """);
    }

    private static void AddColumnIfMissing(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column,
        string definition)
    {
        if (ColumnExists(connection, transaction, table, column))
        {
            return;
        }

        Execute(connection, transaction, $"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    private static bool ColumnExists(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string table,
        string column)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info({table});";

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void RecordAppliedVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO schema_version (version, applied_at)
            VALUES ($version, $applied_at);
            """;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$applied_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record DatabaseMigration(
        int Version,
        Action<SqliteConnection, SqliteTransaction> Apply);
}
