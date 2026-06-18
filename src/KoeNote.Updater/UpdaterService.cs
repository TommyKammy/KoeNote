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
    private const int MsiSuccess = 0;
    private const int MsiSuccessRebootRequired = 3010;

    public async Task<UpdaterExitCode> ExecuteAsync(UpdaterOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ParentProcessId > 0)
        {
            var parentExited = await WaitForParentExitAsync(options, cancellationToken);
            if (!parentExited)
            {
                return WriteResult(
                    UpdaterExitCode.ParentExitTimedOut,
                    options,
                    $"KoeNote did not exit within {options.ParentExitTimeoutSeconds} seconds, so the silent update was canceled.");
            }
        }

        if (!VerifyInstaller(options.MsiPath, options.ExpectedSha256))
        {
            return WriteResult(UpdaterExitCode.VerificationFailed, options, "The MSI did not match the expected SHA256.");
        }

        int installExitCode;
        try
        {
            installExitCode = await processRunner.RunAsync(
                "msiexec.exe",
                [
                    "/i",
                    options.MsiPath,
                    "/qn",
                    "/norestart",
                    "/L*v",
                    options.LogPath,
                    $"INSTALLFOLDER={EnsureTrailingDirectorySeparator(options.InstallFolderPath)}"
                ],
                cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return WriteResult(UpdaterExitCode.InstallFailed, options, $"msiexec could not be started: {exception.Message}");
        }

        if (installExitCode != MsiSuccess)
        {
            var failureMessage = installExitCode == MsiSuccessRebootRequired
                ? "msiexec exited with code 3010. Windows reported that a restart is required to complete installation, so KoeNote was not relaunched."
                : $"msiexec exited with code {installExitCode}.";
            return WriteResult(UpdaterExitCode.InstallFailed, options, failureMessage);
        }

        WriteResult(UpdaterExitCode.Success, options, "Update installed. Relaunching KoeNote.");

        var relaunched = await TryStartAsync(options, cancellationToken);
        if (!relaunched)
        {
            return WriteResult(UpdaterExitCode.RelaunchFailed, options, "The updated KoeNote executable could not be relaunched.");
        }

        return UpdaterExitCode.Success;
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

    private static string EnsureTrailingDirectorySeparator(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar) || fullPath.EndsWith(Path.AltDirectorySeparatorChar)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
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

    private async Task<bool> WaitForParentExitAsync(UpdaterOptions options, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.ParentExitTimeoutSeconds));
        try
        {
            await processRunner.WaitForExitAsync(options.ParentProcessId, timeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
