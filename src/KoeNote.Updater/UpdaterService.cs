using System.IO;
using System.Security.Cryptography;

namespace KoeNote.Updater;

public interface IUpdaterProcessRunner
{
    Task WaitForExitAsync(int processId, CancellationToken cancellationToken);

    Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    Task<bool> StartAsync(string fileName, CancellationToken cancellationToken);
}

public sealed class UpdaterService(
    IUpdaterProcessRunner processRunner,
    IUpdaterProgressReporter? progressReporter = null)
{
    private const int MsiSuccess = 0;
    private const int MsiSuccessRebootRequired = 3010;
    private readonly IUpdaterProgressReporter _progressReporter = progressReporter ?? NullUpdaterProgressReporter.Instance;

    public async Task<UpdaterExitCode> ExecuteAsync(UpdaterOptions options, CancellationToken cancellationToken = default)
    {
        _progressReporter.Show(options);
        if (options.ParentProcessId > 0)
        {
            _progressReporter.ReportStatus(
                "KoeNoteを終了しています",
                "更新を適用するため、起動中のKoeNoteが閉じるのを待っています。");
            var parentExited = await WaitForParentExitAsync(options, cancellationToken);
            if (!parentExited)
            {
                return await FinishAsync(
                    UpdaterExitCode.ParentExitTimedOut,
                    options,
                    $"KoeNote did not exit within {options.ParentExitTimeoutSeconds} seconds, so the silent update was canceled.",
                    "KoeNoteの更新を開始できませんでした",
                    cancellationToken);
            }
        }

        _progressReporter.ReportStatus(
            "インストーラーを確認しています",
            "ダウンロード済みインストーラーの整合性を確認しています。");
        if (!TryVerifyInstaller(options.MsiPath, options.ExpectedSha256, out var verificationFailureMessage))
        {
            return await FinishAsync(
                UpdaterExitCode.VerificationFailed,
                options,
                verificationFailureMessage,
                "KoeNoteの更新を開始できませんでした",
                cancellationToken);
        }

        int installExitCode;
        try
        {
            _progressReporter.ReportStatus(
                "KoeNoteを更新しています",
                "インストール中です。完了後にKoeNoteを自動で再起動します。");
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
            return await FinishAsync(
                UpdaterExitCode.InstallFailed,
                options,
                $"msiexec could not be started: {exception.Message}",
                "KoeNoteの更新に失敗しました",
                cancellationToken);
        }

        if (installExitCode == MsiSuccessRebootRequired)
        {
            return await FinishAsync(
                UpdaterExitCode.PendingReboot,
                options,
                "Update installed, but Windows reported that a restart is required to complete installation. KoeNote was not relaunched.",
                "Windowsの再起動が必要です",
                cancellationToken);
        }

        if (installExitCode != MsiSuccess)
        {
            return await FinishAsync(
                UpdaterExitCode.InstallFailed,
                options,
                $"msiexec exited with code {installExitCode}.",
                "KoeNoteの更新に失敗しました",
                cancellationToken);
        }

        WriteResult(UpdaterExitCode.Success, options, "Update installed. Relaunching KoeNote.");

        _progressReporter.ReportStatus(
            "KoeNoteを再起動しています",
            "更新が完了しました。KoeNoteを起動しています。");
        var relaunched = await TryStartAsync(options, cancellationToken);
        if (!relaunched)
        {
            return await FinishAsync(
                UpdaterExitCode.RelaunchFailed,
                options,
                "The updated KoeNote executable could not be relaunched.",
                "KoeNoteを再起動できませんでした",
                cancellationToken);
        }

        return UpdaterExitCode.Success;
    }

    private static bool TryVerifyInstaller(string path, string expectedSha256, out string failureMessage)
    {
        failureMessage = "The MSI did not match the expected SHA256.";
        if (!File.Exists(path))
        {
            failureMessage = "The MSI could not be found for SHA256 verification.";
            return false;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failureMessage = $"The MSI could not be read for SHA256 verification: {exception.Message}";
            return false;
        }
    }

    private static UpdaterExitCode WriteResult(UpdaterExitCode exitCode, UpdaterOptions options, string message)
    {
        UpdaterResult.Write(options.ResultPath, UpdaterResult.From(exitCode, options, message));
        return exitCode;
    }

    private async Task<UpdaterExitCode> FinishAsync(
        UpdaterExitCode exitCode,
        UpdaterOptions options,
        string message,
        string title,
        CancellationToken cancellationToken)
    {
        WriteResult(exitCode, options, message);
        await _progressReporter.ReportTerminalAsync(
            title,
            $"{message}{Environment.NewLine}{Environment.NewLine}KoeNoteを手動で起動して、必要であればログを確認してください。",
            options.LogPath,
            cancellationToken);
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
