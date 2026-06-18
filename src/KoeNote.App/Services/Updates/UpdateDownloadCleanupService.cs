using System.IO;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateDownloadCleanupOptions(
    TimeSpan VerifiedInstallerRetention,
    TimeSpan TempFileRetention,
    TimeSpan UpdaterLogRetention,
    TimeSpan UpdaterResultRetention)
{
    public static UpdateDownloadCleanupOptions Default { get; } = new(
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(1),
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(30));
}

public sealed record UpdateDownloadCleanupResult(
    int DeletedVerifiedInstallers,
    int DeletedTempFiles,
    int DeletedUpdaterLogs,
    int DeletedUpdaterResults,
    DateTimeOffset CleanedAt);

public interface IUpdateDownloadCleanupService
{
    UpdateDownloadCleanupResult CleanupOldDownloads(DateTimeOffset? now = null);
}

public sealed class UpdateDownloadCleanupService(
    AppPaths paths,
    UpdateDownloadCleanupOptions? options = null) : IUpdateDownloadCleanupService
{
    private readonly UpdateDownloadCleanupOptions _options = options ?? UpdateDownloadCleanupOptions.Default;

    public UpdateDownloadCleanupResult CleanupOldDownloads(DateTimeOffset? now = null)
    {
        var cleanedAt = now ?? DateTimeOffset.Now;
        if (!Directory.Exists(paths.UpdateDownloads))
        {
            return new UpdateDownloadCleanupResult(0, 0, 0, 0, cleanedAt);
        }

        var verifiedCutoff = cleanedAt - _options.VerifiedInstallerRetention;
        var tempCutoff = cleanedAt - _options.TempFileRetention;
        var logCutoff = cleanedAt - _options.UpdaterLogRetention;
        var resultCutoff = cleanedAt - _options.UpdaterResultRetention;
        var deletedVerified = 0;
        var deletedTemp = 0;
        var deletedLogs = 0;
        var deletedResults = 0;

        foreach (var path in Directory.EnumerateFiles(paths.UpdateDownloads))
        {
            var extension = Path.GetExtension(path);
            var isTempDownload = path.EndsWith(".download", StringComparison.OrdinalIgnoreCase);
            var isSeenResult = path.EndsWith(".result.json.seen", StringComparison.OrdinalIgnoreCase);
            var isInvalidResult = path.EndsWith(".result.json.invalid", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase) && !isTempDownload)
            {
                if (!isSeenResult && !isInvalidResult)
                {
                    continue;
                }
            }

            var lastWrite = File.GetLastWriteTimeUtc(path);
            var cutoff = isSeenResult || isInvalidResult
                ? resultCutoff.UtcDateTime
                : isTempDownload
                    ? tempCutoff.UtcDateTime
                    : verifiedCutoff.UtcDateTime;
            if (lastWrite > cutoff)
            {
                continue;
            }

            if (TryDelete(path))
            {
                if (isTempDownload)
                {
                    deletedTemp++;
                }
                else
                {
                    if (isSeenResult || isInvalidResult)
                    {
                        deletedResults++;
                    }
                    else
                    {
                        deletedVerified++;
                    }
                }
            }
        }

        if (Directory.Exists(paths.UpdateLogs))
        {
            foreach (var path in Directory.EnumerateFiles(paths.UpdateLogs, "*.log", SearchOption.TopDirectoryOnly))
            {
                if (File.GetLastWriteTimeUtc(path) > logCutoff.UtcDateTime)
                {
                    continue;
                }

                if (TryDelete(path))
                {
                    deletedLogs++;
                }
            }
        }

        return new UpdateDownloadCleanupResult(deletedVerified, deletedTemp, deletedLogs, deletedResults, cleanedAt);
    }

    private static bool TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
