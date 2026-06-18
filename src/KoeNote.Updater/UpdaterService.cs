using System.Security.Cryptography;

namespace KoeNote.Updater;

public interface IUpdaterProcessRunner
{
    Task WaitForExitAsync(int processId, CancellationToken cancellationToken);

    Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task<bool> StartAsync(string fileName, CancellationToken cancellationToken);
}

public sealed class UpdaterService(IUpdaterProcessRunner processRunner)
{
    public async Task<UpdaterExitCode> ExecuteAsync(UpdaterOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ParentProcessId > 0)
        {
            await processRunner.WaitForExitAsync(options.ParentProcessId, cancellationToken);
        }

        if (!VerifyInstaller(options.MsiPath, options.ExpectedSha256))
        {
            return WriteResult(UpdaterExitCode.VerificationFailed, options, "The MSI did not match the expected SHA256.");
        }

        var installExitCode = await processRunner.RunAsync(
            "msiexec.exe",
            ["/i", options.MsiPath, "/qn", "/norestart", "/L*v", options.LogPath],
            cancellationToken);

        if (installExitCode != 0)
        {
            return WriteResult(UpdaterExitCode.InstallFailed, options, $"msiexec exited with code {installExitCode}.");
        }

        var relaunched = await TryStartAsync(options, cancellationToken);
        if (!relaunched)
        {
            return WriteResult(UpdaterExitCode.RelaunchFailed, options, "The updated KoeNote executable could not be relaunched.");
        }

        return WriteResult(UpdaterExitCode.Success, options, "Update installed and KoeNote relaunched.");
    }

    private static bool VerifyInstaller(string path, string expectedSha256)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private static UpdaterExitCode WriteResult(UpdaterExitCode exitCode, UpdaterOptions options, string message)
    {
        UpdaterResult.Write(options.ResultPath, UpdaterResult.From(exitCode, options, message));
        return exitCode;
    }

    private async Task<bool> TryStartAsync(UpdaterOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await processRunner.StartAsync(options.TargetExePath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }
}
