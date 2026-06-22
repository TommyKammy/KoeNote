using KoeNote.App.Services.Updates;

namespace KoeNote.App.ViewModels;

internal sealed class UpdatePresentationPresenter
{
    public bool HasNotification(string title, string message)
    {
        return !string.IsNullOrWhiteSpace(title) ||
            !string.IsNullOrWhiteSpace(message);
    }

    public bool HasForegroundNotification(string title, string message, bool isSuppressed)
    {
        return !isSuppressed && HasNotification(title, message);
    }

    public bool ShouldSuppressNotificationDuringBackgroundDownload(string title, bool isMandatory)
    {
        return !isMandatory &&
            title.StartsWith("Update available:", StringComparison.Ordinal);
    }

    public bool HasVerifiedInstaller(string installerPath)
    {
        return !string.IsNullOrWhiteSpace(installerPath);
    }

    public bool CanShowInstallAction(string installerPath)
    {
        return HasVerifiedInstaller(installerPath);
    }

    public bool CanShowRestartAction(LatestReleaseInfo? availableUpdate)
    {
        return availableUpdate is not null;
    }

    public string GetRestartActionText(bool isDownloadInProgress)
    {
        return isDownloadInProgress
            ? "Downloading update..."
            : "Update and restart";
    }

    public string GetRestartBlockedReason(LatestReleaseInfo? availableUpdate, bool isRunInProgress)
    {
        return availableUpdate is not null && isRunInProgress
            ? "Finish or cancel the current run before updating and restarting."
            : string.Empty;
    }

    public bool HasRestartBlockedReason(LatestReleaseInfo? availableUpdate, bool isRunInProgress)
    {
        return !string.IsNullOrWhiteSpace(GetRestartBlockedReason(availableUpdate, isRunInProgress));
    }

    public bool CanShowDownloadAction(LatestReleaseInfo? availableUpdate, string installerPath)
    {
        return availableUpdate is not null && !HasVerifiedInstaller(installerPath);
    }

    public bool CanDownload(LatestReleaseInfo? availableUpdate, bool isDownloadInProgress, string installerPath)
    {
        return availableUpdate is not null &&
            !isDownloadInProgress &&
            !HasVerifiedInstaller(installerPath);
    }

    public bool CanInstall(string installerPath, bool isRunInProgress)
    {
        return HasVerifiedInstaller(installerPath) && !isRunInProgress;
    }

    public bool CanRestart(LatestReleaseInfo? availableUpdate, bool isDownloadInProgress, bool isRunInProgress)
    {
        return availableUpdate is not null &&
            !isDownloadInProgress &&
            !isRunInProgress;
    }

    public string FormatDownloadProgress(UpdateDownloadProgress progress)
    {
        if (progress.BytesTotal is > 0)
        {
            var percent = progress.BytesDownloaded * 100d / progress.BytesTotal.Value;
            return $"Downloading update: {percent:0}% ({FormatBytes(progress.BytesDownloaded)} / {FormatBytes(progress.BytesTotal.Value)})";
        }

        return $"Downloading update: {FormatBytes(progress.BytesDownloaded)}";
    }

    public string FormatVerifiedInstallerProgress(string installerPath)
    {
        return $"SHA256 verified installer: {installerPath}";
    }

    public string FormatInstallStartedProgress(string installerPath)
    {
        return $"Update and restart started: {installerPath}";
    }

    public UpdateNotificationPresentation CreateReadyToRestartNotification(string version)
    {
        return new UpdateNotificationPresentation(
            $"Ready to update and restart: KoeNote {version}",
            "The update package has been downloaded and SHA256 verified. Choose Update and restart to apply it.");
    }

    public UpdateNotificationPresentation CreateInstallStartedNotification(string trustDescription)
    {
        return new UpdateNotificationPresentation(
            "Update and restart started",
            $"KoeNote will close so Windows can apply the verified update. Verification: {trustDescription}.");
    }

    public UpdateNotificationPresentation CreateAvailableUpdateNotification(UpdateCheckResult result)
    {
        var version = result.LatestRelease?.Version ?? string.Empty;
        return result.IsMandatory
            ? new UpdateNotificationPresentation(
                $"Required update: KoeNote {version}",
                "A required update is available. Finish current work, then choose Update and restart.")
            : new UpdateNotificationPresentation(
                $"Update available: KoeNote {version}",
                "A newer KoeNote release is available. Choose Update and restart when your current work is saved.");
    }

    public UpdateInstallerResultPresentation CreateInstallerResultPresentation(UpdateInstallerResult result)
    {
        var success = IsSuccessfulInstallerResult(result);
        var version = string.IsNullOrWhiteSpace(result.Version) ? null : result.Version;
        var title = success
            ? string.IsNullOrWhiteSpace(version)
                ? FormatSuccessfulInstallerTitle(result)
                : FormatSuccessfulInstallerTitle(result, version)
            : "Update failed";

        return new UpdateInstallerResultPresentation(
            title,
            FormatInstallerResultMessage(result),
            success);
    }

    private static string FormatInstallerResultMessage(UpdateInstallerResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message)
            ? $"Updater helper exited with code {result.ExitCode}."
            : result.Message;
        return string.IsNullOrWhiteSpace(result.LogPath)
            ? message
            : $"{message} Installer log: {result.LogPath}";
    }

    private static bool IsSuccessfulInstallerResult(UpdateInstallerResult result)
    {
        return result.ExitCode == 0 ||
            string.Equals(result.Status, "PendingReboot", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSuccessfulInstallerTitle(UpdateInstallerResult result)
    {
        return string.Equals(result.Status, "PendingReboot", StringComparison.OrdinalIgnoreCase)
            ? "Restart required to complete update"
            : "Update completed";
    }

    private static string FormatSuccessfulInstallerTitle(UpdateInstallerResult result, string version)
    {
        if (string.Equals(result.Status, "PendingReboot", StringComparison.OrdinalIgnoreCase))
        {
            return $"Restart required to complete update: KoeNote {version}";
        }

        var versionLabel = version.StartsWith("v", StringComparison.OrdinalIgnoreCase)
            ? version
            : "v" + version;
        return $"KoeNote updated to {versionLabel}";
    }

    private static string FormatBytes(long sizeBytes)
    {
        return ModelDownloadProgressPresenter.FormatByteSize(sizeBytes);
    }
}

internal sealed record UpdateNotificationPresentation(string Title, string Message);

internal sealed record UpdateInstallerResultPresentation(string Title, string Message, bool IsSuccessful);
