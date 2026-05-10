using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly TimeSpan ModelCatalogProgressRefreshInterval = TimeSpan.FromSeconds(1);

    private void BeginModelDownloadProgress(string displayName)
    {
        _modelDownloadNotificationTimer.Stop();
        IsModelDownloadInProgress = true;
        IsModelDownloadProgressIndeterminate = false;
        ModelDownloadProgressStageText = string.Empty;
        ModelDownloadProgressPercent = 0;
        ModelDownloadProgressSummary = $"Downloading {displayName}: preparing...";
        IsModelDownloadNotificationError = false;
        ModelDownloadNotification = string.Empty;
        ResetModelCatalogProgressRefresh();
    }

    private void UpdateModelDownloadProgress(string displayName, ModelDownloadProgress progress)
    {
        if (GetUsableDownloadTotal(progress) is { } totalBytes)
        {
            ModelDownloadProgressStageText = string.Empty;
            IsModelDownloadProgressIndeterminate = false;
            ModelDownloadProgressPercent = progress.BytesDownloaded * 100d / totalBytes;
        }
        else
        {
            ModelDownloadProgressStageText = "ダウンロード中";
            IsModelDownloadProgressIndeterminate = IsModelDownloadInProgress;
            ModelDownloadProgressPercent = 0;
        }

        ModelDownloadProgressSummary = $"Downloading {displayName}: {FormatDownloadProgress(progress)}";
        LatestLog = ModelDownloadProgressSummary;
    }

    private void RefreshModelCatalogForDownloadProgress(ModelDownloadProgress progress)
    {
        var now = DateTimeOffset.UtcNow;
        var percent = GetDownloadPercent(progress);
        var isDifferentModel = !string.Equals(
            _lastModelCatalogProgressRefreshModelId,
            progress.ModelId,
            StringComparison.OrdinalIgnoreCase);
        var hasVisiblePercentChange = percent >= 0 && percent != _lastModelCatalogProgressRefreshPercent;
        var hasElapsed = now - _lastModelCatalogProgressRefreshAt >= ModelCatalogProgressRefreshInterval;

        if (!isDifferentModel && !hasVisiblePercentChange && !hasElapsed)
        {
            return;
        }

        _lastModelCatalogProgressRefreshModelId = progress.ModelId;
        _lastModelCatalogProgressRefreshAt = now;
        _lastModelCatalogProgressRefreshPercent = percent;
        RefreshModelCatalogKeepingSelection(progress.ModelId);
    }

    private void ResetModelCatalogProgressRefresh()
    {
        _lastModelCatalogProgressRefreshAt = DateTimeOffset.MinValue;
        _lastModelCatalogProgressRefreshModelId = null;
        _lastModelCatalogProgressRefreshPercent = -1;
    }

    private void CompleteModelDownloadProgress(string displayName, bool succeeded, string? message = null)
    {
        IsModelDownloadInProgress = false;
        IsModelDownloadProgressIndeterminate = false;
        ModelDownloadProgressStageText = string.Empty;
        if (succeeded)
        {
            ModelDownloadProgressPercent = 100;
            ModelDownloadProgressSummary = $"Completed {displayName}: 100%";
            ModelDownloadNotification = $"Download completed: {displayName}";
            IsModelDownloadNotificationError = false;
            LatestLog = $"Model installed and verified: {displayName}";
            ScheduleModelDownloadNotificationDismiss();
            return;
        }

        ModelDownloadProgressSummary = message ?? $"Download stopped: {displayName}";
        ModelDownloadNotification = ModelDownloadProgressSummary;
        IsModelDownloadNotificationError = true;
        LatestLog = ModelDownloadProgressSummary;
        ScheduleModelDownloadNotificationDismiss();
    }

    private void ScheduleModelDownloadNotificationDismiss()
    {
        _modelDownloadNotificationTimer.Stop();
        _modelDownloadNotificationTimer.Start();
    }

    private void ClearModelDownloadNotification()
    {
        ModelDownloadNotification = string.Empty;
        IsModelDownloadNotificationError = false;
    }

    private static string FormatDownloadProgress(ModelDownloadProgress progress)
    {
        if (GetUsableDownloadTotal(progress) is { } totalBytes)
        {
            var percent = progress.BytesDownloaded * 100d / totalBytes;
            return $"{percent:0}% ({FormatBytes(progress.BytesDownloaded)} / {FormatBytes(totalBytes)})";
        }

        return FormatBytes(progress.BytesDownloaded);
    }

    private static long? GetUsableDownloadTotal(ModelDownloadProgress progress)
    {
        return progress.BytesTotal is > 0 && progress.BytesDownloaded <= progress.BytesTotal.Value
            ? progress.BytesTotal
            : null;
    }

    private static int GetDownloadPercent(ModelDownloadProgress progress)
    {
        return GetUsableDownloadTotal(progress) is { } totalBytes
            ? (int)Math.Floor(progress.BytesDownloaded * 100d / totalBytes)
            : -1;
    }

    private static string FormatBytes(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)sizeBytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }
}
