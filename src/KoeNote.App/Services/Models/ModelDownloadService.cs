using System.IO;
using System.Net.Http;

namespace KoeNote.App.Services.Models;

public sealed class ModelDownloadService(
    HttpClient httpClient,
    ModelDownloadJobRepository downloadJobRepository,
    ModelVerificationService verificationService,
    ModelInstallService installService)
{
    public async Task<InstalledModel> DownloadAndInstallAsync(
        ModelCatalogItem catalogItem,
        string targetPath,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(catalogItem.Download.Url))
        {
            throw new InvalidOperationException($"Model has no download URL: {catalogItem.ModelId}");
        }

        if (!IsSupportedDirectDownload(catalogItem))
        {
            throw new InvalidOperationException($"Model download URL is not a direct downloadable artifact: {catalogItem.ModelId}");
        }

        var uri = new Uri(catalogItem.Download.Url);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only HTTPS model downloads are allowed.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var tempPath = $"{targetPath}.partial";
        var downloadId = downloadJobRepository.Start(
            catalogItem.ModelId,
            uri.ToString(),
            targetPath,
            tempPath,
            catalogItem.Download.Sha256);
        return await DownloadAndInstallExistingAsync(catalogItem, downloadId, progress, cancellationToken);
    }

    public async Task<InstalledModel> ResumeDownloadAndInstallAsync(
        ModelCatalogItem catalogItem,
        string downloadId,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await DownloadAndInstallExistingAsync(catalogItem, downloadId, progress, cancellationToken);
    }

    public void Cancel(string downloadId)
    {
        downloadJobRepository.MarkCancelled(downloadId);
    }

    public void Pause(string downloadId)
    {
        downloadJobRepository.MarkPaused(downloadId);
    }

    private async Task<InstalledModel> DownloadAndInstallExistingAsync(
        ModelCatalogItem catalogItem,
        string downloadId,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var job = downloadJobRepository.Find(downloadId)
            ?? throw new InvalidOperationException($"Download job not found: {downloadId}");
        var uri = new Uri(job.Url);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only HTTPS model downloads are allowed.");
        }

        var failureRecorded = false;
        try
        {
            var resumeOffset = File.Exists(job.TempPath) ? new FileInfo(job.TempPath).Length : 0;
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            if (resumeOffset > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeOffset, null);
            }

            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var totalBytes = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;
            var buffer = new byte[1024 * 128];
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(
                job.TempPath,
                resumeOffset > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                long downloaded = output.Position;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloaded += read;
                    downloadJobRepository.UpdateProgress(downloadId, downloaded, totalBytes);
                    progress?.Report(new ModelDownloadProgress(catalogItem.ModelId, downloaded, totalBytes));
                }

                await output.FlushAsync(cancellationToken);
            }

            var verification = verificationService.VerifyPath(job.TempPath, catalogItem.Download.Sha256);
            if (!verification.IsVerified)
            {
                downloadJobRepository.MarkFailed(downloadId, verification.Message, verification.Sha256);
                failureRecorded = true;
                throw new InvalidOperationException(verification.Message);
            }

            if (File.Exists(job.TargetPath))
            {
                File.Delete(job.TargetPath);
            }

            File.Move(job.TempPath, job.TargetPath);
            downloadJobRepository.MarkSucceeded(downloadId, verification.Sha256);
            return installService.RegisterDownloadedModel(catalogItem, job.TargetPath);
        }
        catch (OperationCanceledException)
        {
            downloadJobRepository.MarkCancelled(downloadId);
            throw;
        }
        catch (Exception exception)
        {
            if (!failureRecorded)
            {
                downloadJobRepository.MarkFailed(downloadId, exception.Message);
            }

            throw;
        }
    }

    private static bool IsSupportedDirectDownload(ModelCatalogItem catalogItem)
    {
        var type = catalogItem.Download.Type;
        var url = catalogItem.Download.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (type.Equals("huggingface", StringComparison.OrdinalIgnoreCase))
        {
            return url.Contains("/resolve/", StringComparison.OrdinalIgnoreCase);
        }

        return type.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("direct", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("direct_file", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ModelDownloadProgress(string ModelId, long BytesDownloaded, long? BytesTotal);
