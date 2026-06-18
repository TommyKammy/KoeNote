using KoeNote.App.Services.Updates;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class UpdatePresentationPresenterTests
{
    [Fact]
    public void FormatDownloadProgress_IncludesPercentWhenTotalIsKnown()
    {
        var presenter = new UpdatePresentationPresenter();

        var text = presenter.FormatDownloadProgress(new UpdateDownloadProgress(1536, 3072));

        Assert.Equal("Downloading update: 50% (1.5 KB / 3 KB)", text);
    }

    [Fact]
    public void FormatDownloadProgress_UsesDownloadedBytesWhenTotalIsUnknown()
    {
        var presenter = new UpdatePresentationPresenter();

        var text = presenter.FormatDownloadProgress(new UpdateDownloadProgress(2048, null));

        Assert.Equal("Downloading update: 2 KB", text);
    }

    [Fact]
    public void ActionVisibility_ReflectsUpdateInstallerAndRunState()
    {
        var presenter = new UpdatePresentationPresenter();
        var release = CreateRelease();
        const string installerPath = @"C:\updates\KoeNote.msi";

        Assert.False(presenter.CanShowDownloadAction(null, string.Empty));
        Assert.True(presenter.CanShowDownloadAction(release, string.Empty));
        Assert.False(presenter.CanShowDownloadAction(release, installerPath));
        Assert.True(presenter.CanShowInstallAction(installerPath));
        Assert.True(presenter.CanShowRestartAction(release));
        Assert.True(presenter.CanDownload(release, isDownloadInProgress: false, installerPath: string.Empty));
        Assert.False(presenter.CanDownload(release, isDownloadInProgress: true, installerPath: string.Empty));
        Assert.True(presenter.CanInstall(installerPath, isRunInProgress: false));
        Assert.False(presenter.CanInstall(installerPath, isRunInProgress: true));
        Assert.True(presenter.CanRestart(release, isDownloadInProgress: false, isRunInProgress: false));
        Assert.False(presenter.CanRestart(release, isDownloadInProgress: false, isRunInProgress: true));
    }

    [Fact]
    public void RestartPresentation_ShowsDownloadTextAndBlockedReason()
    {
        var presenter = new UpdatePresentationPresenter();
        var release = CreateRelease();

        Assert.Equal("Downloading update...", presenter.GetRestartActionText(isDownloadInProgress: true));
        Assert.Equal("Update and restart", presenter.GetRestartActionText(isDownloadInProgress: false));
        Assert.Empty(presenter.GetRestartBlockedReason(null, isRunInProgress: true));
        Assert.Contains("Finish or cancel", presenter.GetRestartBlockedReason(release, isRunInProgress: true), StringComparison.Ordinal);
        Assert.True(presenter.HasRestartBlockedReason(release, isRunInProgress: true));
    }

    [Fact]
    public void InstallerResultPresentation_FormatsSuccessFailureAndPendingReboot()
    {
        var presenter = new UpdatePresentationPresenter();

        var success = presenter.CreateInstallerResultPresentation(CreateInstallerResult(
            status: "Completed",
            exitCode: 0,
            version: "0.20.0",
            message: "Update installed.",
            logPath: @"C:\logs\install.log"));
        var pendingReboot = presenter.CreateInstallerResultPresentation(CreateInstallerResult(
            status: "PendingReboot",
            exitCode: 3010,
            version: "0.20.0",
            message: "A restart is required.",
            logPath: string.Empty));
        var failed = presenter.CreateInstallerResultPresentation(CreateInstallerResult(
            status: "Failed",
            exitCode: 3010,
            version: "0.20.0",
            message: string.Empty,
            logPath: @"C:\logs\install.log"));

        Assert.True(success.IsSuccessful);
        Assert.Equal("KoeNote updated to v0.20.0", success.Title);
        Assert.Contains("Installer log: C:\\logs\\install.log", success.Message, StringComparison.Ordinal);
        Assert.True(pendingReboot.IsSuccessful);
        Assert.Equal("Restart required to complete update: KoeNote 0.20.0", pendingReboot.Title);
        Assert.False(failed.IsSuccessful);
        Assert.Equal("Update failed", failed.Title);
        Assert.Contains("Updater helper exited with code 3010.", failed.Message, StringComparison.Ordinal);
        Assert.Contains("Installer log: C:\\logs\\install.log", failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotificationFactories_KeepStableUpdateMessages()
    {
        var presenter = new UpdatePresentationPresenter();
        var release = CreateRelease();
        var available = presenter.CreateAvailableUpdateNotification(new UpdateCheckResult(
            true,
            true,
            false,
            "0.14.0",
            release,
            "KoeNote 0.15.0 is available."));
        var mandatory = presenter.CreateAvailableUpdateNotification(new UpdateCheckResult(
            true,
            true,
            true,
            "0.14.0",
            release,
            "KoeNote 0.15.0 is available."));
        var ready = presenter.CreateReadyToRestartNotification("0.15.0");
        var started = presenter.CreateInstallStartedNotification("SHA256 and signer verified");

        Assert.Equal("Update available: KoeNote 0.15.0", available.Title);
        Assert.Contains("newer KoeNote release", available.Message, StringComparison.Ordinal);
        Assert.Equal("Required update: KoeNote 0.15.0", mandatory.Title);
        Assert.Contains("required update", mandatory.Message, StringComparison.Ordinal);
        Assert.Equal("Ready to update and restart: KoeNote 0.15.0", ready.Title);
        Assert.Contains("SHA256 verified", ready.Message, StringComparison.Ordinal);
        Assert.Equal("Update and restart started", started.Title);
        Assert.Contains("SHA256 and signer verified", started.Message, StringComparison.Ordinal);
    }

    private static LatestReleaseInfo CreateRelease()
    {
        return new LatestReleaseInfo(
            "0.15.0",
            new Uri("https://example.com/KoeNote.msi"),
            "abc123",
            null,
            new Uri("https://example.com/releases/0.15.0"),
            false,
            "win-x64",
            DateTimeOffset.Now);
    }

    private static UpdateInstallerResult CreateInstallerResult(
        string status,
        int exitCode,
        string version,
        string message,
        string logPath)
    {
        return new UpdateInstallerResult(
            @"C:\updates\result.json",
            status,
            exitCode,
            version,
            @"C:\updates\KoeNote.msi",
            @"C:\Program Files\KoeNote\KoeNote.exe",
            logPath,
            DateTimeOffset.Now,
            message);
    }
}
