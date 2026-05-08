using KoeNote.App.Services.Diagnostics;

namespace KoeNote.App.Tests;

public sealed class CrashLogServiceTests
{
    [Fact]
    public void WriteAppStartLog_WritesRuntimeLog()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var service = new CrashLogService(paths);

        var path = service.WriteAppStartLog();

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("Source: AppStart", content);
        Assert.Contains("AppVersion:", content);
    }

    [Fact]
    public void WriteExceptionLog_WritesExceptionDetails()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var service = new CrashLogService(paths);

        var path = service.WriteExceptionLog("test", CreateException());

        var content = File.ReadAllText(path);
        Assert.Contains("Source: test", content);
        Assert.Contains("ExceptionType: System.InvalidOperationException", content);
        Assert.Contains("boom", content);
        Assert.Contains(nameof(CreateException), content);
    }

    [Fact]
    public void ReadRecentCrashLogs_ReturnsNewestCrashLogs()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var service = new CrashLogService(paths);

        var older = service.WriteExceptionLog("older", new InvalidOperationException("older"));
        Thread.Sleep(5);
        var newer = service.WriteExceptionLog("newer", new InvalidOperationException("newer"));

        var logs = service.ReadRecentCrashLogs(1);

        Assert.Single(logs);
        Assert.Equal(newer, logs[0].Path);
        Assert.Contains("newer", logs[0].Content);
        Assert.NotEqual(older, logs[0].Path);
    }

    private static Exception CreateException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
