using System.Diagnostics;
using System.IO;
using System.Net.Http;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string UpdateNotificationTitle
    {
        get => _updateNotificationTitle;
        private set
        {
            if (SetField(ref _updateNotificationTitle, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasUpdateNotification));
            }
        }
    }

    public string UpdateNotificationMessage
    {
        get => _updateNotificationMessage;
        private set
        {
            if (SetField(ref _updateNotificationMessage, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasUpdateNotification));
            }
        }
    }

    public bool HasUpdateNotification =>
        !string.IsNullOrWhiteSpace(UpdateNotificationTitle) ||
        !string.IsNullOrWhiteSpace(UpdateNotificationMessage);

    public bool IsUpdateMandatory => _availableUpdate?.Mandatory == true;

    public Uri? AvailableUpdateReleaseNotesUrl => _availableUpdate?.ReleaseNotesUrl;

    public string AvailableUpdateVersion => _availableUpdate?.Version ?? string.Empty;

    public string UpdateDownloadProgressText
    {
        get => _updateDownloadProgressText;
        private set => SetField(ref _updateDownloadProgressText, value ?? string.Empty);
    }

    public string VerifiedUpdateInstallerPath
    {
        get => _verifiedUpdateInstallerPath;
        private set
        {
            if (SetField(ref _verifiedUpdateInstallerPath, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasVerifiedUpdateInstaller));
                OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
                OnPropertyChanged(nameof(CanShowInstallUpdateAction));
                OnPropertyChanged(nameof(UpdateRestartActionText));
                if (DownloadUpdateCommand is RelayCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }

                if (InstallVerifiedUpdateCommand is RelayCommand installCommand)
                {
                    installCommand.RaiseCanExecuteChanged();
                }

                if (UpdateAndRestartCommand is RelayCommand updateAndRestartCommand)
                {
                    updateAndRestartCommand.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public bool HasVerifiedUpdateInstaller => !string.IsNullOrWhiteSpace(VerifiedUpdateInstallerPath);

    public bool CanShowInstallUpdateAction => HasVerifiedUpdateInstaller;

    public bool CanShowUpdateRestartAction => _availableUpdate is not null;

    public string UpdateRestartActionText => IsUpdateDownloadInProgress
        ? "Downloading update..."
        : "Update and restart";

    public string UpdateRestartBlockedReason => _availableUpdate is not null && IsRunInProgress
        ? "Finish or cancel the current run before updating and restarting."
        : string.Empty;

    public bool HasUpdateRestartBlockedReason => !string.IsNullOrWhiteSpace(UpdateRestartBlockedReason);

    public bool IsUpdateCheckInProgress
    {
        get => _isUpdateCheckInProgress;
        private set
        {
            if (SetField(ref _isUpdateCheckInProgress, value) &&
                CheckForUpdatesCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsUpdateDownloadInProgress
    {
        get => _isUpdateDownloadInProgress;
        private set
        {
            if (SetField(ref _isUpdateDownloadInProgress, value))
            {
                if (DownloadUpdateCommand is RelayCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }

                if (UpdateAndRestartCommand is RelayCommand updateAndRestartCommand)
                {
                    updateAndRestartCommand.RaiseCanExecuteChanged();
                }

                OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
                OnPropertyChanged(nameof(UpdateRestartActionText));
            }
        }
    }

    public bool CanShowUpdateDownloadAction => _availableUpdate is not null && !HasVerifiedUpdateInstaller;

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await _updateCheckService.CheckAsync();
            RecordUpdateHistory("check_startup_completed", result.CurrentVersion, result.Message);
            ApplyUpdateCheckResult(
                result,
                showUpToDate: false,
                preserveExistingNotification: _hasPendingUpdateInstallerResult);
            if (!_hasPendingUpdateInstallerResult && HasDownloadableUpdate(result))
            {
                StartBackgroundUpdateDownloadIfEligible();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            RecordUpdateHistory("check_startup_failed", null, exception.Message);
            LatestLog = $"Update check skipped: {exception.Message}";
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsUpdateCheckInProgress)
        {
            return;
        }

        IsUpdateCheckInProgress = true;
        LatestLog = "Checking for KoeNote updates...";
        RecordUpdateHistory("check_started", null, "Manual update check started.");
        try
        {
            var result = await _updateCheckService.CheckAsync();
            RecordUpdateHistory("check_completed", result.CurrentVersion, result.Message, result.LatestRelease);
            ApplyUpdateCheckResult(result, showUpToDate: true);
            if (HasDownloadableUpdate(result))
            {
                StartBackgroundUpdateDownloadIfEligible();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            RecordUpdateHistory("check_failed", null, exception.Message);
            InvalidateUpdateDownloadSnapshot();
            _availableUpdate = null;
            ClearVerifiedUpdateInstallerState();
            UpdateNotificationTitle = "Update check failed";
            UpdateNotificationMessage = exception.Message;
            OnPropertyChanged(nameof(IsUpdateMandatory));
            OnPropertyChanged(nameof(AvailableUpdateVersion));
            OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
            RefreshUpdateRestartState();
            RefreshUpdateCommandStates();
            LatestLog = $"Update check failed: {exception.Message}";
        }
        finally
        {
            IsUpdateCheckInProgress = false;
        }
    }

    private Task DismissUpdateNotificationAsync()
    {
        InvalidateUpdateDownloadSnapshot();
        _availableUpdate = null;
        UpdateNotificationTitle = string.Empty;
        UpdateNotificationMessage = string.Empty;
        UpdateDownloadProgressText = string.Empty;
        VerifiedUpdateInstallerPath = string.Empty;
        OnPropertyChanged(nameof(IsUpdateMandatory));
        OnPropertyChanged(nameof(AvailableUpdateVersion));
        OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
        OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
        OnPropertyChanged(nameof(CanShowInstallUpdateAction));
        RefreshUpdateRestartState();
        RefreshUpdateCommandStates();
        return Task.CompletedTask;
    }

    private async Task UpdateAndRestartAsync()
    {
        if (_availableUpdate is null || IsUpdateDownloadInProgress)
        {
            return;
        }

        if (IsRunInProgress)
        {
            UpdateNotificationMessage = UpdateRestartBlockedReason;
            return;
        }

        if (!HasVerifiedUpdateInstaller)
        {
            await DownloadUpdateAsync();
        }

        if (HasVerifiedUpdateInstaller)
        {
            await InstallVerifiedUpdateAsync();
        }
    }

    private async Task DownloadUpdateAsync(bool isBackground = false)
    {
        var release = _availableUpdate;
        if (release is null || IsUpdateDownloadInProgress)
        {
            return;
        }

        var generation = isBackground ? BeginUpdateDownloadSnapshot() : _updateDownloadGeneration;
        IsUpdateDownloadInProgress = true;
        VerifiedUpdateInstallerPath = string.Empty;
        UpdateDownloadProgressText = "Downloading update...";
        if (isBackground)
        {
            UpdateNotificationMessage = "Downloading the update in the background. You can continue working until it is ready to restart.";
            LatestLog = $"Downloading KoeNote {release.Version} update in the background...";
        }
        else
        {
            LatestLog = $"Downloading KoeNote {release.Version} update...";
        }

        RecordUpdateHistory(
            isBackground ? "download_background_started" : "download_started",
            release.Version,
            isBackground ? "Background update download started." : "Update download started.");
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(downloadProgress =>
            {
                if (IsCurrentUpdateDownload(release, generation))
                {
                    UpdateDownloadProgressText = FormatUpdateDownloadProgress(downloadProgress);
                }
            });
            var result = await _updateDownloadService.DownloadAndVerifyAsync(release, progress);
            if (!IsCurrentUpdateDownload(release, generation))
            {
                return;
            }

            VerifiedUpdateInstallerPath = result.FilePath;
            UpdateDownloadProgressText = $"SHA256 verified installer: {result.FilePath}";
            UpdateNotificationTitle = $"Ready to update and restart: KoeNote {release.Version}";
            UpdateNotificationMessage = "The update package has been downloaded and SHA256 verified. Choose Update and restart to apply it.";
            LatestLog = $"Update downloaded and verified: {result.FilePath}";
            RecordUpdateHistory("download_verified", release.Version, "Update installer downloaded and SHA256 verified.", result.FilePath, result.Sha256);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or TaskCanceledException)
        {
            if (!IsCurrentUpdateDownload(release, generation))
            {
                return;
            }

            RecordUpdateHistory("download_failed", release.Version, exception.Message);
            UpdateDownloadProgressText = string.Empty;
            UpdateNotificationTitle = "Update download failed";
            UpdateNotificationMessage = exception.Message;
            LatestLog = $"Update download failed: {exception.Message}";
        }
        finally
        {
            IsUpdateDownloadInProgress = false;
            OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
        }
    }

    private void StartBackgroundUpdateDownloadIfEligible()
    {
        if (_availableUpdate is null ||
            IsUpdateDownloadInProgress ||
            HasVerifiedUpdateInstaller ||
            IsBackgroundUpdateDownloadDisabled())
        {
            return;
        }

        _ = DownloadUpdateAsync(isBackground: true);
    }

    private int BeginUpdateDownloadSnapshot()
    {
        return ++_updateDownloadGeneration;
    }

    private void InvalidateUpdateDownloadSnapshot()
    {
        _updateDownloadGeneration++;
    }

    private bool IsCurrentUpdateDownload(LatestReleaseInfo release, int generation)
    {
        return generation == _updateDownloadGeneration && ReferenceEquals(_availableUpdate, release);
    }

    private static bool HasDownloadableUpdate(UpdateCheckResult result)
    {
        return result.IsConfigured && result.IsUpdateAvailable && result.LatestRelease is not null;
    }

    private bool CanDownloadUpdate()
    {
        return _availableUpdate is not null && !IsUpdateDownloadInProgress && !HasVerifiedUpdateInstaller;
    }

    private static bool IsBackgroundUpdateDownloadDisabled()
    {
        var value = Environment.GetEnvironmentVariable("KOENOTE_DISABLE_BACKGROUND_UPDATE_DOWNLOAD");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private void SurfacePendingUpdateResult()
    {
        var result = _updateResultService.ConsumeLatestResult();
        if (result is null)
        {
            return;
        }

        _hasPendingUpdateInstallerResult = true;
        var version = string.IsNullOrWhiteSpace(result.Version) ? null : result.Version;
        if (IsSuccessfulUpdateInstallerResult(result))
        {
            UpdateNotificationTitle = string.IsNullOrWhiteSpace(version)
                ? FormatSuccessfulUpdateInstallerTitle(result)
                : $"{FormatSuccessfulUpdateInstallerTitle(result)}: KoeNote {version}";
            UpdateNotificationMessage = FormatUpdateInstallerResultMessage(result);
            LatestLog = $"Update completed: {result.Message}";
            RecordUpdateHistory("install_completed", version, result.Message, result.InstallerPath);
            return;
        }

        UpdateNotificationTitle = "Update failed";
        UpdateNotificationMessage = FormatUpdateInstallerResultMessage(result);
        LatestLog = $"Update failed: {result.Message}";
        RecordUpdateHistory("install_failed", version, result.Message, result.InstallerPath);
    }

    private static string FormatUpdateInstallerResultMessage(UpdateInstallerResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message)
            ? $"Updater helper exited with code {result.ExitCode}."
            : result.Message;
        return string.IsNullOrWhiteSpace(result.LogPath)
            ? message
            : $"{message} Installer log: {result.LogPath}";
    }

    private static bool IsSuccessfulUpdateInstallerResult(UpdateInstallerResult result)
    {
        return result.ExitCode == 0 ||
            string.Equals(result.Status, "PendingReboot", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSuccessfulUpdateInstallerTitle(UpdateInstallerResult result)
    {
        return string.Equals(result.Status, "PendingReboot", StringComparison.OrdinalIgnoreCase)
            ? "Restart required to complete update"
            : "Update completed";
    }

    private bool CanUpdateAndRestart()
    {
        return _availableUpdate is not null && !IsUpdateDownloadInProgress && !IsRunInProgress;
    }

    private Task InstallVerifiedUpdateAsync()
    {
        if (!CanInstallVerifiedUpdate())
        {
            return Task.CompletedTask;
        }

        try
        {
            var result = _updateInstallerLauncher.Launch(
                VerifiedUpdateInstallerPath,
                _availableUpdate?.Sha256,
                AvailableUpdateVersion);
            ClearVerifiedUpdateInstallerState();
            UpdateDownloadProgressText = $"Update and restart started: {result.InstallerPath}";
            UpdateNotificationTitle = "Update and restart started";
            UpdateNotificationMessage = $"KoeNote will close so Windows can apply the verified update. Verification: {result.TrustDescription}.";
            LatestLog = $"Update installer started: {result.InstallerPath}";
            RecordUpdateHistory("install_started", AvailableUpdateVersion, $"Update installer started. Verification: {result.TrustDescription}", result.InstallerPath);
            _shutdownApplication();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            RecordUpdateHistory("install_failed", AvailableUpdateVersion, exception.Message, VerifiedUpdateInstallerPath);
            UpdateNotificationTitle = "Update installer could not start";
            UpdateNotificationMessage = exception.Message;
            LatestLog = $"Update installer launch failed: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    private bool CanInstallVerifiedUpdate()
    {
        return HasVerifiedUpdateInstaller && !IsRunInProgress;
    }

    private Task OpenUpdateReleaseNotesAsync()
    {
        if (AvailableUpdateReleaseNotesUrl is { } uri)
        {
            OpenUriAction(uri);
        }

        return Task.CompletedTask;
    }

    private void ApplyUpdateCheckResult(
        UpdateCheckResult result,
        bool showUpToDate,
        bool preserveExistingNotification = false)
    {
        if (!result.IsConfigured)
        {
            LatestLog = result.Message;
            return;
        }

        if (!result.IsUpdateAvailable || result.LatestRelease is null)
        {
            if (showUpToDate)
            {
                LatestLog = result.Message;
                InvalidateUpdateDownloadSnapshot();
                _availableUpdate = null;
                ClearVerifiedUpdateInstallerState();
                UpdateNotificationTitle = "KoeNote is up to date";
                UpdateNotificationMessage = result.Message;
                OnPropertyChanged(nameof(IsUpdateMandatory));
                OnPropertyChanged(nameof(AvailableUpdateVersion));
                OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
                OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
                RefreshUpdateRestartState();
                RefreshUpdateCommandStates();
            }

            return;
        }

        _availableUpdate = result.LatestRelease;
        VerifiedUpdateInstallerPath = string.Empty;
        UpdateDownloadProgressText = string.Empty;
        if (!preserveExistingNotification)
        {
            UpdateNotificationTitle = result.IsMandatory
                ? $"Required update: KoeNote {result.LatestRelease.Version}"
                : $"Update available: KoeNote {result.LatestRelease.Version}";
            UpdateNotificationMessage = result.IsMandatory
                ? "A required update is available. Finish current work, then choose Update and restart."
                : "A newer KoeNote release is available. Choose Update and restart when your current work is saved.";
        }

        OnPropertyChanged(nameof(IsUpdateMandatory));
        OnPropertyChanged(nameof(AvailableUpdateVersion));
        OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
        OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
        RefreshUpdateRestartState();
        RefreshUpdateCommandStates();

        if (!preserveExistingNotification)
        {
            LatestLog = result.Message;
        }
    }

    private void ClearVerifiedUpdateInstallerState()
    {
        VerifiedUpdateInstallerPath = string.Empty;
        UpdateDownloadProgressText = string.Empty;
        OnPropertyChanged(nameof(CanShowInstallUpdateAction));
        OnPropertyChanged(nameof(UpdateRestartActionText));
    }

    private static string FormatUpdateDownloadProgress(UpdateDownloadProgress progress)
    {
        if (progress.BytesTotal is > 0)
        {
            var percent = progress.BytesDownloaded * 100d / progress.BytesTotal.Value;
            return $"Downloading update: {percent:0}% ({FormatBytes(progress.BytesDownloaded)} / {FormatBytes(progress.BytesTotal.Value)})";
        }

        return $"Downloading update: {FormatBytes(progress.BytesDownloaded)}";
    }

    private void RefreshUpdateCommandStates()
    {
        if (OpenUpdateReleaseNotesCommand is RelayCommand releaseNotesCommand)
        {
            releaseNotesCommand.RaiseCanExecuteChanged();
        }

        if (DownloadUpdateCommand is RelayCommand downloadCommand)
        {
            downloadCommand.RaiseCanExecuteChanged();
        }

        if (InstallVerifiedUpdateCommand is RelayCommand installCommand)
        {
            installCommand.RaiseCanExecuteChanged();
        }

        if (UpdateAndRestartCommand is RelayCommand updateAndRestartCommand)
        {
            updateAndRestartCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshUpdateRestartState()
    {
        OnPropertyChanged(nameof(CanShowUpdateRestartAction));
        OnPropertyChanged(nameof(UpdateRestartActionText));
        OnPropertyChanged(nameof(UpdateRestartBlockedReason));
        OnPropertyChanged(nameof(HasUpdateRestartBlockedReason));
    }

    private void RecordUpdateHistory(
        string eventName,
        string? version,
        string message,
        LatestReleaseInfo? release = null)
    {
        RecordUpdateHistory(eventName, release?.Version ?? version, message, null, release?.Sha256);
    }

    private void RecordUpdateHistory(
        string eventName,
        string? version,
        string message,
        string? installerPath,
        string? sha256 = null)
    {
        try
        {
            _updateHistoryService.Record(new UpdateHistoryEntry(
                DateTimeOffset.Now,
                eventName,
                message,
                version,
                installerPath,
                sha256));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
        }
    }

    private static void OpenUriInShell(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
