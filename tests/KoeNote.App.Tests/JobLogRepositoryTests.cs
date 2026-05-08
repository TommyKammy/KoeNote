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

    [Fact]
    public void ReadForJob_ReturnsAllSelectedJobLogsInChronologicalOrder()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

        var repository = new JobLogRepository(paths);
        repository.AddEvent("job-001", "created", "info", "first");
        Thread.Sleep(5);
        repository.AddEvent("job-002", "created", "info", "other");
        Thread.Sleep(5);
        repository.AddEvent("job-001", "asr", "error", "second");

        var logs = repository.ReadForJob("job-001");

        Assert.Equal(2, logs.Count);
        Assert.Equal("first", logs[0].Message);
        Assert.Equal("second", logs[1].Message);
        Assert.All(logs, entry => Assert.NotEqual("other", entry.Message));
    }

    [Fact]
    public void ReadForDiagnostics_WithJobId_ReturnsSelectedJobWithJobId()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

        var repository = new JobLogRepository(paths);
        repository.AddEvent("job-001", "created", "info", "first");
        Thread.Sleep(5);
        repository.AddEvent("job-002", "created", "info", "other");

        var logs = repository.ReadForDiagnostics("job-001");

        Assert.Single(logs);
        Assert.Equal("job-001", logs[0].JobId);
        Assert.Equal("first", logs[0].Message);
    }

    [Fact]
    public void ReadForDiagnostics_WithoutJobId_ReturnsRecentLogsAcrossJobs()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

        var repository = new JobLogRepository(paths);
        repository.AddEvent("job-001", "created", "info", "first");
        Thread.Sleep(5);
        repository.AddEvent("job-002", "asr", "error", "second");

        var logs = repository.ReadForDiagnostics(null);

        Assert.Equal(2, logs.Count);
        Assert.Equal("job-001", logs[0].JobId);
        Assert.Equal("job-002", logs[1].JobId);
    }

}
