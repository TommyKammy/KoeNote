using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

internal sealed class ModelCatalogPresenter
{
    public ModelCatalogEntry? ResolveSelection(
        IEnumerable<ModelCatalogEntry> entries,
        string? preferredModelId)
    {
        var entryList = entries as IReadOnlyList<ModelCatalogEntry> ?? entries.ToArray();
        if (string.IsNullOrWhiteSpace(preferredModelId))
        {
            return entryList.FirstOrDefault();
        }

        return entryList.FirstOrDefault(entry =>
            entry.ModelId.Equals(preferredModelId, StringComparison.OrdinalIgnoreCase)) ?? entryList.FirstOrDefault();
    }

    public bool CanDownload(ModelCatalogEntry? entry)
    {
        return entry is { IsInstalled: false } &&
            entry.IsDirectDownloadSupported &&
            !IsDownloadRunning(entry.LatestDownloadJob);
    }

    public bool CanPause(ModelCatalogEntry? entry)
    {
        return IsDownloadRunning(entry?.LatestDownloadJob);
    }

    public bool CanResume(ModelCatalogEntry? entry)
    {
        return entry?.LatestDownloadJob is { Status: "paused" };
    }

    public bool CanCancel(ModelCatalogEntry? entry)
    {
        return entry?.LatestDownloadJob is { Status: "running" or "paused" };
    }

    public bool CanRetry(ModelCatalogEntry? entry)
    {
        return entry is { IsInstalled: false, LatestDownloadJob.Status: "failed" or "cancelled" } &&
            entry.IsDirectDownloadSupported;
    }

    public bool CanDeleteFiles(ModelCatalogEntry? entry, bool isRunInProgress, bool isModelDownloadInProgress)
    {
        return entry is { IsInstalled: true } &&
            !isRunInProgress &&
            !isModelDownloadInProgress &&
            !IsDownloadRunning(entry.LatestDownloadJob);
    }

    private static bool IsDownloadRunning(ModelDownloadJob? job)
    {
        return string.Equals(job?.Status, "running", StringComparison.OrdinalIgnoreCase);
    }
}
