using System.IO;
using KoeNote.App.Services.Database;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public sealed class DatabaseInitializer(AppPaths paths)
{
    private readonly DatabaseMigrator _migrator = new(KoeNoteDatabaseMigrations.All);

    public void EnsureCreated()
    {
        var databaseExists = File.Exists(paths.DatabasePath);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        if (databaseExists)
        {
            EnsureDatabaseIntegrity(connection);
            var existingAppliedVersions = _migrator.ReadExistingAppliedVersions(connection);
            _migrator.ThrowIfUnsupportedNewerSchema(existingAppliedVersions);

            if (_migrator.HasPendingMigrations(existingAppliedVersions))
            {
                new UpdateBackupService(paths).CreateBeforeMigrationBackup(
                    existingAppliedVersions.Count == 0 ? 0 : existingAppliedVersions.Max(),
                    _migrator.LatestVersion);
            }
        }

        Execute(connection, "PRAGMA journal_mode=WAL;");

        _migrator.ApplyPendingMigrations(connection);
        EnsureDatabaseIntegrity(connection);
    }

    private static void EnsureDatabaseIntegrity(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        var result = command.ExecuteScalar() as string;
        if (!string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"KoeNote database integrity check failed: {result}");
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
