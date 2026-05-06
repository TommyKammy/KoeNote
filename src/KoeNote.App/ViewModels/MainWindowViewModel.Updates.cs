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
                if (DownloadUpdateCommand is RelayCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }

                if (InstallVerifiedUpdateCommand is RelayCommand installCommand)
                {
                    installCommand.RaiseCanExecuteChanged();
                }
            }
        }
    }

    public bool HasVerifiedUpdateInstaller => !string.IsNullOrWhiteSpace(VerifiedUpdateInstallerPath);

    public bool CanShowInstallUpdateAction => HasVerifiedUpdateInstaller;

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

                OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
            }
        }
    }

    public bool CanShowUpdateDownloadAction => _availableUpdate is not null && !HasVerifiedUpdateInstaller;

    private async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var result = await _updateCheckService.CheckAsync();
            ApplyUpdateCheckResult(result, showUpToDate: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
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
        try
        {
            var result = await _updateCheckService.CheckAsync();
            ApplyUpdateCheckResult(result, showUpToDate: true);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            _availableUpdate = null;
            ClearVerifiedUpdateInstallerState();
            UpdateNotificationTitle = "Update check failed";
            UpdateNotificationMessage = exception.Message;
            OnPropertyChanged(nameof(IsUpdateMandatory));
            OnPropertyChanged(nameof(AvailableUpdateVersion));
            OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
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
        RefreshUpdateCommandStates();
        return Task.CompletedTask;
    }

    private async Task DownloadUpdateAsync()
    {
        if (_availableUpdate is null || IsUpdateDownloadInProgress)
        {
            return;
        }

        IsUpdateDownloadInProgress = true;
        VerifiedUpdateInstallerPath = string.Empty;
        UpdateDownloadProgressText = "Downloading update...";
        LatestLog = $"Downloading KoeNote {AvailableUpdateVersion} update...";
        try
        {
            var progress = new Progress<UpdateDownloadProgress>(downloadProgress =>
            {
                UpdateDownloadProgressText = FormatUpdateDownloadProgress(downloadProgress);
            });
            var result = await _updateDownloadService.DownloadAndVerifyAsync(_availableUpdate, progress);
            VerifiedUpdateInstallerPath = result.FilePath;
            UpdateDownloadProgressText = $"Verified installer: {result.FilePath}";
            UpdateNotificationTitle = $"Update ready: KoeNote {AvailableUpdateVersion}";
            UpdateNotificationMessage = "The installer has been downloaded and verified. Finish current work before installing it.";
            LatestLog = $"Update downloaded and verified: {result.FilePath}";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or UnauthorizedAccessException or InvalidOperationException or TaskCanceledException)
        {
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

    private bool CanDownloadUpdate()
    {
        return _availableUpdate is not null && !IsUpdateDownloadInProgress && !HasVerifiedUpdateInstaller;
    }

    private Task InstallVerifiedUpdateAsync()
    {
        if (!CanInstallVerifiedUpdate())
        {
            return Task.CompletedTask;
        }

        try
        {
            var result = _updateInstallerLauncher.Launch(VerifiedUpdateInstallerPath);
            ClearVerifiedUpdateInstallerState();
            UpdateDownloadProgressText = $"Installer started: {result.InstallerPath}";
            UpdateNotificationTitle = "Update installer started";
            UpdateNotificationMessage = "Follow the installer prompts to complete the update.";
            LatestLog = $"Update installer started: {result.InstallerPath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
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

    private void ApplyUpdateCheckResult(UpdateCheckResult result, bool showUpToDate)
    {
        if (!result.IsConfigured)
        {
            LatestLog = result.Message;
            return;
        }

        if (!result.IsUpdateAvailable || result.LatestRelease is null)
        {
            LatestLog = result.Message;
            if (showUpToDate)
            {
                _availableUpdate = null;
                ClearVerifiedUpdateInstallerState();
                UpdateNotificationTitle = "KoeNote is up to date";
                UpdateNotificationMessage = result.Message;
                OnPropertyChanged(nameof(IsUpdateMandatory));
                OnPropertyChanged(nameof(AvailableUpdateVersion));
                OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
                OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
                RefreshUpdateCommandStates();
            }

            return;
        }

        _availableUpdate = result.LatestRelease;
        VerifiedUpdateInstallerPath = string.Empty;
        UpdateDownloadProgressText = string.Empty;
        UpdateNotificationTitle = result.IsMandatory
            ? $"Required update: KoeNote {result.LatestRelease.Version}"
            : $"Update available: KoeNote {result.LatestRelease.Version}";
        UpdateNotificationMessage = result.IsMandatory
            ? "A required update is available. Finish current work, then install the latest release."
            : "A newer KoeNote release is available. Review the release notes when you are ready.";
        OnPropertyChanged(nameof(IsUpdateMandatory));
        OnPropertyChanged(nameof(AvailableUpdateVersion));
        OnPropertyChanged(nameof(AvailableUpdateReleaseNotesUrl));
        OnPropertyChanged(nameof(CanShowUpdateDownloadAction));
        RefreshUpdateCommandStates();

        LatestLog = result.Message;
    }

    private void ClearVerifiedUpdateInstallerState()
    {
        VerifiedUpdateInstallerPath = string.Empty;
        UpdateDownloadProgressText = string.Empty;
        OnPropertyChanged(nameof(CanShowInstallUpdateAction));
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
