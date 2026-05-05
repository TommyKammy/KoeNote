using System.IO;
using System.Net.Http;

namespace KoeNote.App.Services.Models;

public sealed class ModelDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly ModelDownloadJobRepository _downloadJobRepository;
    private readonly ModelVerificationService _verificationService;
    private readonly ModelInstallService _installService;
    private readonly IReadOnlyList<IModelDownloadStrategy> _downloadStrategies;

    public ModelDownloadService(
        HttpClient httpClient,
        ModelDownloadJobRepository downloadJobRepository,
        ModelVerificationService verificationService,
        ModelInstallService installService,
        IReadOnlyList<IModelDownloadStrategy>? downloadStrategies = null)
    {
        _httpClient = httpClient;
        _downloadJobRepository = downloadJobRepository;
        _verificationService = verificationService;
        _installService = installService;
        _downloadStrategies = downloadStrategies ?? [
            new HuggingFaceRepositoryDownloadStrategy(),
            new DirectModelDownloadStrategy()
        ];
    }

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

        if (!HasSupportedDownloadStrategy(catalogItem))
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
        var downloadId = _downloadJobRepository.Start(
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
        _downloadJobRepository.MarkCancelled(downloadId);
    }

    public void Pause(string downloadId)
    {
        _downloadJobRepository.MarkPaused(downloadId);
    }

    private async Task<InstalledModel> DownloadAndInstallExistingAsync(
        ModelCatalogItem catalogItem,
        string downloadId,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var job = _downloadJobRepository.Find(downloadId)
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
            var strategy = SelectDownloadStrategy(catalogItem);
            await strategy.DownloadAsync(
                new ModelDownloadStrategyContext(catalogItem, job, downloadId, _httpClient, _downloadJobRepository, progress),
                cancellationToken);

            var verification = _verificationService.VerifyPath(job.TempPath, catalogItem.Download.Sha256);
            if (!verification.IsVerified)
            {
                _downloadJobRepository.MarkFailed(downloadId, verification.Message, verification.Sha256);
                failureRecorded = true;
                throw new InvalidOperationException(verification.Message);
            }

            MoveDownloadedArtifact(job.TempPath, job.TargetPath);
            _downloadJobRepository.MarkSucceeded(downloadId, verification.Sha256);
            return _installService.RegisterDownloadedModel(catalogItem, job.TargetPath);
        }
        catch (OperationCanceledException)
        {
            var current = _downloadJobRepository.Find(downloadId);
            if (!string.Equals(current?.Status, "paused", StringComparison.OrdinalIgnoreCase))
            {
                _downloadJobRepository.MarkCancelled(downloadId);
            }

            throw;
        }
        catch (Exception exception)
        {
            if (!failureRecorded)
            {
                _downloadJobRepository.MarkFailed(downloadId, exception.Message);
            }

            throw;
        }
    }

    private bool HasSupportedDownloadStrategy(ModelCatalogItem catalogItem)
    {
        return _downloadStrategies.Any(strategy => strategy.CanHandle(catalogItem));
    }

    private IModelDownloadStrategy SelectDownloadStrategy(ModelCatalogItem catalogItem)
    {
        return _downloadStrategies.FirstOrDefault(strategy => strategy.CanHandle(catalogItem))
            ?? throw new InvalidOperationException($"Model download URL is not supported: {catalogItem.ModelId}");
    }

    private static void MoveDownloadedArtifact(string tempPath, string targetPath)
    {
        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, recursive: true);
        }
        else if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        if (Directory.Exists(tempPath))
        {
            Directory.Move(tempPath, targetPath);
            return;
        }

        if (File.Exists(tempPath))
        {
            File.Move(tempPath, targetPath);
        }
    }
}

public sealed record ModelDownloadProgress(string ModelId, long BytesDownloaded, long? BytesTotal);
