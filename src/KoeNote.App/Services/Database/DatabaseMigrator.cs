using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Database;

internal sealed class DatabaseMigrator
{
    private readonly IReadOnlyList<DatabaseMigration> _migrations;

    public DatabaseMigrator(IReadOnlyList<DatabaseMigration> migrations)
    {
        ArgumentNullException.ThrowIfNull(migrations);
        var orderedMigrations = migrations
            .OrderBy(static migration => migration.Version)
            .ToArray();
        var duplicate = orderedMigrations
            .GroupBy(static migration => migration.Version)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null)
        {
            var names = string.Join(", ", duplicate.Select(static migration => migration.Name));
            throw new InvalidOperationException($"Duplicate database migration version {duplicate.Key}: {names}");
        }

        _migrations = orderedMigrations;
    }

    public void ApplyPendingMigrations(SqliteConnection connection)
    {
        EnsureSchemaVersionTable(connection);
        var appliedVersions = ReadAppliedVersions(connection);
        foreach (var migration in _migrations)
        {
            if (appliedVersions.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = connection.BeginTransaction();
            migration.Apply(new DatabaseMigrationContext(connection, transaction));
            RecordAppliedVersion(connection, transaction, migration.Version);
            transaction.Commit();
            appliedVersions.Add(migration.Version);
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
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();
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
}
