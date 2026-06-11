using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

internal sealed class ModelDownloadProgressPresenter
{
    private static readonly TimeSpan CatalogRefreshInterval = TimeSpan.FromSeconds(1);

    private DateTimeOffset _lastCatalogRefreshAt = DateTimeOffset.MinValue;
    private string? _lastCatalogRefreshModelId;
    private int _lastCatalogRefreshPercent = -1;

    public ModelDownloadProgressViewState Begin(string displayName)
    {
        ResetCatalogRefresh();
        return new ModelDownloadProgressViewState(
            Summary: $"Downloading {displayName}: preparing...",
            StageText: string.Empty,
            Percent: 0,
            IsInProgress: true,
            IsIndeterminate: false,
            Notification: string.Empty,
            IsNotificationError: false,
            LatestLog: null);
    }

    public ModelDownloadProgressViewState Update(string displayName, ModelDownloadProgress progress, bool isDownloadInProgress)
    {
        var totalBytes = GetUsableDownloadTotal(progress);
        var stageText = totalBytes.HasValue ? string.Empty : "ダウンロード中";
        var percent = totalBytes.HasValue ? progress.BytesDownloaded * 100d / totalBytes.Value : 0;
        var summary = $"Downloading {displayName}: {FormatDownloadProgress(progress)}";

        return new ModelDownloadProgressViewState(
            Summary: summary,
            StageText: stageText,
            Percent: percent,
            IsInProgress: null,
            IsIndeterminate: !totalBytes.HasValue && isDownloadInProgress,
            Notification: null,
            IsNotificationError: null,
            LatestLog: summary);
    }

    public ModelDownloadProgressViewState Complete(string displayName, bool succeeded, string? message = null)
    {
        if (succeeded)
        {
            return new ModelDownloadProgressViewState(
                Summary: $"Completed {displayName}: 100%",
                StageText: string.Empty,
                Percent: 100,
                IsInProgress: false,
                IsIndeterminate: false,
                Notification: $"Download completed: {displayName}",
                IsNotificationError: false,
                LatestLog: $"Model installed and verified: {displayName}");
        }

        var summary = message ?? $"Download stopped: {displayName}";
        return new ModelDownloadProgressViewState(
            Summary: summary,
            StageText: string.Empty,
            Percent: null,
            IsInProgress: false,
            IsIndeterminate: false,
            Notification: summary,
            IsNotificationError: true,
            LatestLog: summary);
    }

    public bool ShouldRefreshCatalog(ModelDownloadProgress progress, DateTimeOffset now)
    {
        var percent = GetDownloadPercent(progress);
        var isDifferentModel = !string.Equals(
            _lastCatalogRefreshModelId,
            progress.ModelId,
            StringComparison.OrdinalIgnoreCase);
        var hasVisiblePercentChange = percent >= 0 && percent != _lastCatalogRefreshPercent;
        var hasElapsed = now - _lastCatalogRefreshAt >= CatalogRefreshInterval;

        if (!isDifferentModel && !hasVisiblePercentChange && !hasElapsed)
        {
            return false;
        }

        _lastCatalogRefreshModelId = progress.ModelId;
        _lastCatalogRefreshAt = now;
        _lastCatalogRefreshPercent = percent;
        return true;
    }

    public void ResetCatalogRefresh()
    {
        _lastCatalogRefreshAt = DateTimeOffset.MinValue;
        _lastCatalogRefreshModelId = null;
        _lastCatalogRefreshPercent = -1;
    }

    public static string FormatByteSize(long sizeBytes)
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

    private static string FormatDownloadProgress(ModelDownloadProgress progress)
    {
        if (GetUsableDownloadTotal(progress) is { } totalBytes)
        {
            var percent = progress.BytesDownloaded * 100d / totalBytes;
            return $"{percent:0}% ({FormatByteSize(progress.BytesDownloaded)} / {FormatByteSize(totalBytes)})";
        }

        return FormatByteSize(progress.BytesDownloaded);
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
}

internal sealed record ModelDownloadProgressViewState(
    string? Summary,
    string? StageText,
    double? Percent,
    bool? IsInProgress,
    bool? IsIndeterminate,
    string? Notification,
    bool? IsNotificationError,
    string? LatestLog);
