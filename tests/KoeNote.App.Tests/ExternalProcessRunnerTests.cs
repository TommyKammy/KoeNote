using KoeNote.App.Services;

namespace KoeNote.App.Tests;

public sealed class ExternalProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_ReturnsStdoutAndExitCode()
    {
        var runner = new ExternalProcessRunner();

        var result = await runner.RunAsync(
            "dotnet",
            "--version",
            TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("11.0", result.StandardOutput);
    }

    [Fact]
    public async Task RunAsync_TimesOutAndKillsProcess()
    {
        var runner = new ExternalProcessRunner();

        await Assert.ThrowsAsync<TimeoutException>(() => runner.RunAsync(
            "powershell",
            "-NoProfile -Command Start-Sleep -Seconds 5",
            TimeSpan.FromMilliseconds(100)));
    }
}
