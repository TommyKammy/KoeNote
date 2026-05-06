using System.Net.Http;
using System.Security.Cryptography;
using System.IO;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateDownloadProgress(long BytesDownloaded, long? BytesTotal);

public sealed record UpdateDownloadResult(
    string FilePath,
    string Sha256,
    long BytesDownloaded,
    DateTimeOffset VerifiedAt);

public interface IUpdateDownloadService
{
    Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        LatestReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed class UpdateDownloadService(
    HttpClient httpClient,
    AppPaths paths,
    IUpdateDownloadCleanupService? cleanupService = null) : IUpdateDownloadService
{
    private readonly IUpdateDownloadCleanupService _cleanupService = cleanupService ?? new UpdateDownloadCleanupService(paths);

    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        LatestReleaseInfo release,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);
        if (!string.Equals(release.MsiUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update MSI URL must use HTTPS.");
        }

        Directory.CreateDirectory(paths.UpdateDownloads);
        _cleanupService.CleanupOldDownloads();

        var fileName = GetSafeMsiFileName(release);
        var finalPath = Path.Combine(paths.UpdateDownloads, fileName);
        var tempPath = finalPath + ".download";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            using var response = await httpClient.GetAsync(release.MsiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentLength;
            await using (var remoteStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var fileStream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                while (true)
                {
                    var read = await remoteStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    progress?.Report(new UpdateDownloadProgress(downloaded, totalBytes));
                }
            }

            var actualHash = (await ComputeSha256Async(tempPath, cancellationToken)).ToLowerInvariant();
            if (!string.Equals(actualHash, release.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Downloaded update SHA256 mismatch. Expected {release.Sha256} but got {actualHash}.");
            }

            if (File.Exists(finalPath))
            {
                File.Delete(finalPath);
            }

            File.Move(tempPath, finalPath);
            var length = new FileInfo(finalPath).Length;
            progress?.Report(new UpdateDownloadProgress(length, length));
            return new UpdateDownloadResult(finalPath, actualHash, length, DateTimeOffset.Now);
        }
        catch
        {
            DeleteTempFileIfExists(tempPath);
            throw;
        }
    }

    private static string GetSafeMsiFileName(LatestReleaseInfo release)
    {
        var fileName = Path.GetFileName(release.MsiUrl.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName) || !fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            fileName = $"KoeNote-v{release.Version}-{release.RuntimeIdentifier}.msi";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalidChar, '_');
        }

        return fileName;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void DeleteTempFileIfExists(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
