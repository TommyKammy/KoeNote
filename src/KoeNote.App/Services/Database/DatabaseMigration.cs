using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Database;

internal sealed record DatabaseMigration(
    int Version,
    string Name,
    Action<DatabaseMigrationContext> Apply);

internal sealed class DatabaseMigrationContext(
    SqliteConnection connection,
    SqliteTransaction transaction)
{
    public void Execute(string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void AddColumnIfMissing(string table, string column, string definition)
    {
        if (ColumnExists(table, column))
        {
            return;
        }

        Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition};");
    }

    public void ExecuteIfTableExists(string table, string sql)
    {
        if (TableExists(table))
        {
            Execute(sql);
        }
    }

    private bool ColumnExists(string table, string column)
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

    private bool TableExists(string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT 1
            FROM sqlite_master
            WHERE type = 'table' AND name = $table;
            """;
        command.Parameters.AddWithValue("$table", table);
        return command.ExecuteScalar() is not null;
    }
}
