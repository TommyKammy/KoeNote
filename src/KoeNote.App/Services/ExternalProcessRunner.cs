using System.Diagnostics;

namespace KoeNote.App.Services;

public sealed record ProcessRunResult(int ExitCode, TimeSpan Duration, string StandardOutput, string StandardError);

public sealed class ExternalProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(
        string fileName,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var start = DateTimeOffset.UtcNow;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            },
            EnableRaisingEvents = true
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var exitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeout, cancellationToken);

        var completed = await Task.WhenAny(exitTask, timeoutTask);
        if (completed == timeoutTask)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{fileName} timed out after {timeout}.");
        }

        return new ProcessRunResult(
            process.ExitCode,
            DateTimeOffset.UtcNow - start,
            await outputTask,
            await errorTask);
    }
}
