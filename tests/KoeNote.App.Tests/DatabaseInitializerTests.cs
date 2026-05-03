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
}
