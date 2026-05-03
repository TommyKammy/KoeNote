using KoeNote.App.Services;
using KoeNote.App.Services.Jobs;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class JobLogRepositoryTests
{
    [Fact]
    public void SaveWorkerLog_WritesLogFileAndDbEvent()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repository = new JobLogRepository(paths);
        var logPath = repository.SaveWorkerLog("job-001", "preprocess", "stdout text 日本語", "stderr text 音声変換");

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("stdout text 日本語", content);
        Assert.Contains("stderr text 音声変換", content);

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM job_log_events WHERE job_id = 'job-001' AND stage = 'preprocess';";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void ReadLatest_ReturnsSelectedJobLogsInChronologicalOrder()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repository = new JobLogRepository(paths);
        repository.AddEvent("job-001", "created", "info", "first");
        Thread.Sleep(5);
        repository.AddEvent("job-002", "created", "info", "other");
        Thread.Sleep(5);
        repository.AddEvent("job-001", "asr", "info", "second");

        var logs = repository.ReadLatest("job-001");

        Assert.Equal(2, logs.Count);
        Assert.Equal("first", logs[0].Message);
        Assert.Equal("second", logs[1].Message);
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
    }

    private static SqliteConnection Open(AppPaths paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }
}
