using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace KoeNote.App.Services.Models;

public interface IModelDownloadStrategy
{
    bool CanHandle(ModelCatalogItem catalogItem);

    Task DownloadAsync(ModelDownloadStrategyContext context, CancellationToken cancellationToken);
}

public sealed record ModelDownloadStrategyContext(
    ModelCatalogItem CatalogItem,
    ModelDownloadJob Job,
    string DownloadId,
    HttpClient HttpClient,
    ModelDownloadJobRepository DownloadJobRepository,
    IProgress<ModelDownloadProgress>? Progress);

public sealed class DirectModelDownloadStrategy : IModelDownloadStrategy
{
    public bool CanHandle(ModelCatalogItem catalogItem)
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

    public async Task DownloadAsync(ModelDownloadStrategyContext context, CancellationToken cancellationToken)
    {
        var job = context.Job;
        var resumeOffset = File.Exists(job.TempPath) ? new FileInfo(job.TempPath).Length : 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(job.Url));
        if (resumeOffset > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(resumeOffset, null);
        }

        using var response = await context.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentRange?.Length ?? response.Content.Headers.ContentLength;
        var progressReporter = new ModelDownloadProgressReporter(context);
        var buffer = new byte[1024 * 128];
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(
            job.TempPath,
            resumeOffset > 0 && response.StatusCode == System.Net.HttpStatusCode.PartialContent ? FileMode.Append : FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        long downloaded = output.Position;
        progressReporter.Report(downloaded, totalBytes, force: true);
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                progressReporter.Report(downloaded, totalBytes);
            }

            await output.FlushAsync(cancellationToken);
        }
        finally
        {
            progressReporter.Report(downloaded, totalBytes, force: true);
        }
    }
}

public sealed class HuggingFaceRepositoryDownloadStrategy : IModelDownloadStrategy
{
    public bool CanHandle(ModelCatalogItem catalogItem)
    {
        return catalogItem.Download.Type.Equals("huggingface", StringComparison.OrdinalIgnoreCase) &&
            HuggingFaceDownloadUrl.TryParseRepositoryId(catalogItem.Download.Url) is not null;
    }

    public async Task DownloadAsync(ModelDownloadStrategyContext context, CancellationToken cancellationToken)
    {
        var catalogItem = context.CatalogItem;
        var job = context.Job;
        var repoId = HuggingFaceDownloadUrl.TryParseRepositoryId(catalogItem.Download.Url)
            ?? throw new InvalidOperationException($"Hugging Face repository URL is not supported: {catalogItem.Download.Url}");
        var apiUri = new Uri($"https://huggingface.co/api/models/{repoId}");
        using var metadata = await context.HttpClient.GetAsync(apiUri, cancellationToken);
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
            : catalogItem.SizeBytes;
        var downloadedBytes = CalculateDirectorySize(job.TempPath);
        totalBytes = ModelDownloadProgressMath.NormalizeTotalBytes(downloadedBytes, totalBytes);
        var progressReporter = new ModelDownloadProgressReporter(context);
        progressReporter.Report(downloadedBytes, totalBytes, force: true);

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

            if (existingBytes > 0)
            {
                downloadedBytes -= existingBytes;
                totalBytes = ModelDownloadProgressMath.NormalizeTotalBytes(downloadedBytes, totalBytes);
                progressReporter.Report(downloadedBytes, totalBytes, force: true);
            }

            var fileUri = new Uri($"https://huggingface.co/{repoId}/resolve/main/{Uri.EscapeDataString(file.RelativePath).Replace("%2F", "/", StringComparison.Ordinal)}");
            using var response = await context.HttpClient.GetAsync(fileUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            if (!file.SizeBytes.HasValue && totalBytes is null && response.Content.Headers.ContentLength is { } contentLength)
            {
                totalBytes = downloadedBytes + contentLength;
                totalBytes = ModelDownloadProgressMath.NormalizeTotalBytes(downloadedBytes, totalBytes);
                progressReporter.Report(downloadedBytes, totalBytes, force: true);
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            var buffer = new byte[1024 * 128];
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    downloadedBytes += read;
                    totalBytes = ModelDownloadProgressMath.NormalizeTotalBytes(downloadedBytes, totalBytes);
                    progressReporter.Report(downloadedBytes, totalBytes);
                }
            }
            finally
            {
                progressReporter.Report(downloadedBytes, totalBytes, force: true);
            }
        }
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
}

internal sealed class ModelDownloadProgressReporter
{
    private const long ByteStep = 8L * 1024 * 1024;
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(1);

    private readonly ModelDownloadStrategyContext _context;
    private DateTimeOffset _lastReportedAt = DateTimeOffset.MinValue;
    private long _lastReportedBytes = -1;
    private long? _lastReportedTotalBytes;

    public ModelDownloadProgressReporter(ModelDownloadStrategyContext context)
    {
        _context = context;
    }

    public void Report(long downloadedBytes, long? totalBytes, bool force = false)
    {
        if (!force && !ShouldReport(downloadedBytes, totalBytes))
        {
            return;
        }

        _lastReportedAt = DateTimeOffset.UtcNow;
        _lastReportedBytes = downloadedBytes;
        _lastReportedTotalBytes = totalBytes;
        _context.DownloadJobRepository.UpdateProgress(_context.DownloadId, downloadedBytes, totalBytes);
        _context.Progress?.Report(new ModelDownloadProgress(_context.CatalogItem.ModelId, downloadedBytes, totalBytes));
    }

    private bool ShouldReport(long downloadedBytes, long? totalBytes)
    {
        if (_lastReportedBytes < 0)
        {
            return true;
        }

        if (_lastReportedTotalBytes != totalBytes)
        {
            return true;
        }

        if (downloadedBytes - _lastReportedBytes >= ByteStep)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - _lastReportedAt >= ReportInterval;
    }
}

internal static class ModelDownloadProgressMath
{
    public static long? NormalizeTotalBytes(long downloadedBytes, long? totalBytes)
    {
        return totalBytes is > 0 && downloadedBytes <= totalBytes.Value
            ? totalBytes
            : null;
    }
}

internal static class HuggingFaceDownloadUrl
{
    public static string? TryParseRepositoryId(string? url)
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

internal sealed record HuggingFaceFile(string RelativePath, long? SizeBytes);
