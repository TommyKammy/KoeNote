using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

internal sealed class ModelCatalogPresenter
{
    public ModelCatalogEntry? RefreshEntries(
        ICollection<ModelCatalogEntry> targetEntries,
        IEnumerable<ModelCatalogEntry> sourceEntries,
        string? preferredModelId)
    {
        targetEntries.Clear();
        foreach (var entry in sourceEntries)
        {
            targetEntries.Add(entry);
        }

        return ResolveSelection(targetEntries, preferredModelId);
    }

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

    public IReadOnlyList<AsrEngineOption> BuildAsrEngineOptions(
        IEnumerable<IAsrEngine> engines,
        Func<string, bool> isInstalled)
    {
        return engines
            .Where(static engine => IsUserSelectableAsrEngine(engine.EngineId))
            .Select(engine => new AsrEngineOption(
                engine.EngineId,
                engine.DisplayName,
                isInstalled(engine.EngineId)))
            .ToArray();
    }

    public ModelCatalogEntry? FindInstalledEntry(
        IEnumerable<ModelCatalogEntry> entries,
        string role,
        Func<ModelCatalogEntry, bool> predicate)
    {
        return entries.FirstOrDefault(entry =>
            entry.IsInstalled &&
            entry.IsVerified &&
            entry.InstalledModel is not null &&
            entry.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            predicate(entry));
    }

    public static bool IsUserSelectableAsrEngine(string? engineId)
    {
        return engineId is "kotoba-whisper-v2.2-faster"
            or "whisper-base"
            or "whisper-small"
            or "faster-whisper-large-v3-turbo"
            or "faster-whisper-large-v3";
    }

    public bool CanDownload(ModelCatalogEntry? entry)
    {
        return entry is { IsInstalled: false } &&
            ModelCatalogCompatibility.IsSelectable(entry.CatalogItem) &&
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
            ModelCatalogCompatibility.IsSelectable(entry.CatalogItem) &&
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
