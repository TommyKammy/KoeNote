using KoeNote.App.Services.Database;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public sealed class DatabaseInitializer(AppPaths paths)
{
    private readonly DatabaseMigrator _migrator = new(KoeNoteDatabaseMigrations.All);

    public void EnsureCreated()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        Execute(connection, "PRAGMA journal_mode=WAL;");
        _migrator.ApplyPendingMigrations(connection);
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
