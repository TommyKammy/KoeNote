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
    public async Task RunAsync_DecodesUtf8Output()
    {
        var runner = new ExternalProcessRunner();

        var result = await runner.RunAsync(
            "powershell",
            "-NoProfile -Command \"[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; Write-Output '推敲候補'\"",
            TimeSpan.FromSeconds(10));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("推敲候補", result.StandardOutput);
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

    [Fact]
    public async Task RunAsync_CancelsAndKillsProcess()
    {
        var runner = new ExternalProcessRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            "powershell",
            "-NoProfile -Command Start-Sleep -Seconds 5",
            TimeSpan.FromSeconds(10),
            cancellation.Token));
    }
}
