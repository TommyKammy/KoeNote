using System.IO;
using System.Net.Http;
using System.Text.Json;

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

        if (!IsSupportedDownload(catalogItem))
        {
            throw new InvalidOperationException($"Model download URL is not a downloadable artifact or Hugging Face repository: {catalogItem.ModelId}");
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
        progress?.Report(new ModelDownloadProgress(catalogItem.ModelId, job.BytesDownloaded, job.BytesTotal));
        cancellationToken.ThrowIfCancellationRequested();
        var uri = new Uri(job.Url);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only HTTPS model downloads are allowed.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(job.TempPath)!);

        var failureRecorded = false;
        try
        {
            if (IsHuggingFaceRepository(catalogItem))
            {
                return await DownloadHuggingFaceRepositoryAsync(catalogItem, downloadId, job, progress, cancellationToken);
            }

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
            var current = downloadJobRepository.Find(downloadId);
            if (!string.Equals(current?.Status, "paused", StringComparison.OrdinalIgnoreCase))
            {
                downloadJobRepository.MarkCancelled(downloadId);
            }

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

    private async Task<InstalledModel> DownloadHuggingFaceRepositoryAsync(
        ModelCatalogItem catalogItem,
        string downloadId,
        ModelDownloadJob job,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var repoId = ParseHuggingFaceRepositoryId(catalogItem.Download.Url)
            ?? throw new InvalidOperationException($"Hugging Face repository URL is not supported: {catalogItem.Download.Url}");
        var apiUri = new Uri($"https://huggingface.co/api/models/{repoId}");
        using var metadata = await httpClient.GetAsync(apiUri, cancellationToken);
        metadata.EnsureSuccessStatusCode();
        await using var metadataStream = await metadata.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await ParseHuggingFaceMetadataAsync(metadataStream, repoId, cancellationToken);
        if (!document.RootElement.TryGetProperty("siblings", out var siblings) ||
            siblings.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Hugging Face model metadata has no file list: {repoId}");
        }

        var files = siblings
            .EnumerateArray()
            .Select(ReadHuggingFaceFile)
            .Where(static file => file is not null)
            .Select(static file => file!)
            .Where(static file => !file.RelativePath.Equals(".gitattributes", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException($"Hugging Face repository has no downloadable files: {repoId}");
        }

        Directory.CreateDirectory(job.TempPath);
        var totalBytes = files.All(static file => file.SizeBytes.HasValue)
            ? files.Sum(static file => file.SizeBytes!.Value)
            : (long?)null;
        var downloadedBytes = CalculateDirectorySize(job.TempPath);
        downloadJobRepository.UpdateProgress(downloadId, downloadedBytes, totalBytes);
        progress?.Report(new ModelDownloadProgress(catalogItem.ModelId, downloadedBytes, totalBytes));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = Path.Combine(job.TempPath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            var existingBytes = File.Exists(destinationPath) ? new FileInfo(destinationPath).Length : 0;
            if (file.SizeBytes is { } sizeBytes && existingBytes == sizeBytes)
            {
                continue;
            }

            var fileUri = new Uri($"https://huggingface.co/{repoId}/resolve/main/{Uri.EscapeDataString(file.RelativePath).Replace("%2F", "/", StringComparison.Ordinal)}");
            using var response = await httpClient.GetAsync(fileUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 128];
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloadedBytes += read;
                downloadJobRepository.UpdateProgress(downloadId, downloadedBytes, totalBytes);
                progress?.Report(new ModelDownloadProgress(catalogItem.ModelId, downloadedBytes, totalBytes));
            }
        }

        var verification = verificationService.VerifyPath(job.TempPath, catalogItem.Download.Sha256);
        if (!verification.IsVerified)
        {
            downloadJobRepository.MarkFailed(downloadId, verification.Message, verification.Sha256);
            throw new InvalidOperationException(verification.Message);
        }

        if (Directory.Exists(job.TargetPath))
        {
            Directory.Delete(job.TargetPath, recursive: true);
        }
        else if (File.Exists(job.TargetPath))
        {
            File.Delete(job.TargetPath);
        }

        Directory.Move(job.TempPath, job.TargetPath);
        downloadJobRepository.MarkSucceeded(downloadId, verification.Sha256);
        return installService.RegisterDownloadedModel(catalogItem, job.TargetPath);
    }

    private static HuggingFaceFile? ReadHuggingFaceFile(JsonElement element)
    {
        if (!element.TryGetProperty("rfilename", out var nameProperty) ||
            nameProperty.GetString() is not { Length: > 0 } relativePath)
        {
            return null;
        }

        long? sizeBytes = null;
        if (element.TryGetProperty("size", out var sizeProperty) &&
            sizeProperty.ValueKind == JsonValueKind.Number &&
            sizeProperty.TryGetInt64(out var parsedSize))
        {
            sizeBytes = parsedSize;
        }

        return new HuggingFaceFile(relativePath, sizeBytes);
    }

    private static async Task<JsonDocument> ParseHuggingFaceMetadataAsync(
        Stream metadataStream,
        string repoId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Hugging Face model metadata could not be read: {repoId}", exception);
        }
    }

    private static long CalculateDirectorySize(string directoryPath)
    {
        return Directory.Exists(directoryPath)
            ? Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).Sum(static path => new FileInfo(path).Length)
            : 0;
    }

    private static bool IsSupportedDownload(ModelCatalogItem catalogItem)
    {
        var type = catalogItem.Download.Type;
        var url = catalogItem.Download.Url;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (type.Equals("huggingface", StringComparison.OrdinalIgnoreCase))
        {
            return url.Contains("/resolve/", StringComparison.OrdinalIgnoreCase) ||
                ParseHuggingFaceRepositoryId(url) is not null;
        }

        return type.Equals("https", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("direct", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("direct_file", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHuggingFaceRepository(ModelCatalogItem catalogItem)
    {
        return catalogItem.Download.Type.Equals("huggingface", StringComparison.OrdinalIgnoreCase) &&
            catalogItem.Download.Url is { } url &&
            !url.Contains("/resolve/", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ParseHuggingFaceRepositoryId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2 ||
            segments[0].Equals("models", StringComparison.OrdinalIgnoreCase) ||
            segments.Contains("resolve", StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return $"{segments[0]}/{segments[1]}";
    }
}

public sealed record ModelDownloadProgress(string ModelId, long BytesDownloaded, long? BytesTotal);

internal sealed record HuggingFaceFile(string RelativePath, long? SizeBytes);
