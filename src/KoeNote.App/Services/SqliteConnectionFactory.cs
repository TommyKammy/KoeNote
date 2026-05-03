using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public static class SqliteConnectionFactory
{
    public static SqliteConnection Open(AppPaths paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }
}
