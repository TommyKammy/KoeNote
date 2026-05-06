using System.IO;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateDownloadCleanupOptions(
    TimeSpan VerifiedInstallerRetention,
    TimeSpan TempFileRetention)
{
    public static UpdateDownloadCleanupOptions Default { get; } = new(
        TimeSpan.FromDays(30),
        TimeSpan.FromDays(1));
}

public sealed record UpdateDownloadCleanupResult(
    int DeletedVerifiedInstallers,
    int DeletedTempFiles,
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
            return new UpdateDownloadCleanupResult(0, 0, cleanedAt);
        }

        var verifiedCutoff = cleanedAt - _options.VerifiedInstallerRetention;
        var tempCutoff = cleanedAt - _options.TempFileRetention;
        var deletedVerified = 0;
        var deletedTemp = 0;

        foreach (var path in Directory.EnumerateFiles(paths.UpdateDownloads))
        {
            var extension = Path.GetExtension(path);
            var isTempDownload = path.EndsWith(".download", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(extension, ".msi", StringComparison.OrdinalIgnoreCase) && !isTempDownload)
            {
                continue;
            }

            var lastWrite = File.GetLastWriteTimeUtc(path);
            var cutoff = isTempDownload ? tempCutoff.UtcDateTime : verifiedCutoff.UtcDateTime;
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
                    deletedVerified++;
                }
            }
        }

        return new UpdateDownloadCleanupResult(deletedVerified, deletedTemp, cleanedAt);
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
