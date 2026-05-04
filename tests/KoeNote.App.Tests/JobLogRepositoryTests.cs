using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Tests;

public sealed class JobLogRepositoryTests
{
    [Fact]
    public void SaveWorkerLog_WritesLogFileAndDbEvent()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new JobLogRepository(paths);
        var logPath = repository.SaveWorkerLog("job-001", "preprocess", "stdout text 日本語", "stderr text 音声変換");

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("stdout text 日本語", content);
        Assert.Contains("stderr text 音声変換", content);

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM job_log_events WHERE job_id = 'job-001' AND stage = 'preprocess';";
        Assert.Equal(1L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void ReadLatest_ReturnsSelectedJobLogsInChronologicalOrder()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

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

}
