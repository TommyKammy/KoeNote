using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace KoeNote.App.Services.Review;

public sealed class CudaReviewRuntimeService(AppPaths paths, HttpClient httpClient, CudaReviewRuntimeOptions? options = null)
{
    public const string RuntimeUrlEnvironmentVariable = "KOENOTE_CUDA_REVIEW_RUNTIME_URL";
    public const string RuntimeSha256EnvironmentVariable = "KOENOTE_CUDA_REVIEW_RUNTIME_SHA256";
    public const string DefaultRuntimeUrl = "https://github.com/TommyKammy/KoeNote/releases/latest/download/koenote-cuda-review-runtime.zip";
    public const string FailureCategoryConfigurationMissing = "ConfigurationMissing";
    public const string FailureCategoryCpuRuntimeMissing = "CpuRuntimeMissing";
    public const string FailureCategoryNetworkUnavailable = "NetworkUnavailable";
    public const string FailureCategoryHashMismatch = "HashMismatch";
    public const string FailureCategoryArchiveInvalid = "ArchiveInvalid";
    public const string FailureCategoryInstallFailed = "InstallFailed";

    private static readonly string[] CudaFilePatterns =
    [
        "ggml-cuda*.dll",
        "cublas*.dll",
        "cudart*.dll",
        "cufft*.dll",
        "curand*.dll",
        "cusparse*.dll"
    ];

    private readonly CudaReviewRuntimeOptions _options = options ?? CudaReviewRuntimeOptions.FromEnvironment();

    public bool IsInstalled()
    {
        return CudaReviewRuntimeLayout.HasPackage(paths);
    }

    public async Task<CudaReviewRuntimeInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled())
        {
            return CudaReviewRuntimeInstallResult.Succeeded("CUDA review runtime is already installed.", paths.ReviewRuntimeDirectory);
        }

        if (!File.Exists(paths.LlamaCompletionPath))
        {
            return CudaReviewRuntimeInstallResult.Failed(
                "CPU review runtime must be installed before adding CUDA review runtime files.",
                paths.ReviewRuntimeDirectory,
                FailureCategoryCpuRuntimeMissing);
        }

        if (string.IsNullOrWhiteSpace(_options.RuntimeUrl))
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime URL is not configured. Set {RuntimeUrlEnvironmentVariable} before installing.",
                paths.ReviewRuntimeDirectory,
                FailureCategoryConfigurationMissing);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();

        try
        {
            await DownloadAsync(tempPath, cancellationToken);
            var actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.Sha256) &&
                !actualSha256.Equals(_options.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    $"CUDA review runtime SHA256 mismatch. Expected {_options.Sha256}, got {actualSha256}.",
                    paths.ReviewRuntimeDirectory,
                    FailureCategoryHashMismatch,
                    actualSha256);
            }

            Directory.CreateDirectory(stagingRoot);
            using (var archive = ZipFile.OpenRead(tempPath))
            {
                var cudaEntries = GetCudaEntries(archive).ToList();
                if (cudaEntries.Count == 0)
                {
                    return CudaReviewRuntimeInstallResult.Failed(
                        "CUDA review runtime archive did not contain CUDA runtime DLLs.",
                        paths.ReviewRuntimeDirectory,
                        FailureCategoryArchiveInvalid,
                        actualSha256);
                }

                if (!cudaEntries.Any(static entry => entry.Name.StartsWith("ggml-cuda", StringComparison.OrdinalIgnoreCase)))
                {
                    return CudaReviewRuntimeInstallResult.Failed(
                        "CUDA review runtime archive did not contain ggml-cuda*.dll.",
                        paths.ReviewRuntimeDirectory,
                        FailureCategoryArchiveInvalid,
                        actualSha256);
                }

                ExtractEntries(cudaEntries, stagingRoot);
            }

            Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
            Directory.CreateDirectory(backupRoot);

            foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(stagingRoot, stagedFile);
                var destinationPath = Path.Combine(paths.ReviewRuntimeDirectory, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                if (File.Exists(destinationPath))
                {
                    var backupPath = Path.Combine(backupRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(destinationPath, backupPath, overwrite: true);
                }

                File.Copy(stagedFile, destinationPath, overwrite: true);
                copiedFiles.Add(destinationPath);
            }

            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, actualSha256);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded($"CUDA review runtime installed: {paths.ReviewRuntimeDirectory}", paths.ReviewRuntimeDirectory, actualSha256)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were copied, but installation verification failed.",
                    paths.ReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    actualSha256);
        }
        catch (HttpRequestException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime download failed: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime archive could not be read: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime install failed: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime install failed: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            DeleteIfExists(tempPath);
            DeleteDirectoryIfExists(stagingRoot);
            DeleteDirectoryIfExists(backupRoot);
        }
    }

    private async Task DownloadAsync(string tempPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(_options.RuntimeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static IEnumerable<ZipArchiveEntry> GetCudaEntries(ZipArchive archive)
    {
        return archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            CudaFilePatterns.Any(pattern => MatchesPattern(entry.Name, pattern)));
    }

    private static void ExtractEntries(IReadOnlyCollection<ZipArchiveEntry> entries, string stagingRoot)
    {
        var root = Path.GetFullPath(stagingRoot);
        foreach (var entry in entries)
        {
            var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(root, Path.GetFileName(relativePath)));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry escapes the install directory: {entry.FullName}");
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true);
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Task RollBackAsync(IReadOnlyCollection<string> copiedFiles, string backupRoot)
    {
        foreach (var copiedFile in copiedFiles)
        {
            var relativePath = Path.GetFileName(copiedFile);
            var backupPath = Path.Combine(backupRoot, relativePath);
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, copiedFile, overwrite: true);
            }
            else if (File.Exists(copiedFile))
            {
                File.Delete(copiedFile);
            }
        }

        return Task.CompletedTask;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

public sealed record CudaReviewRuntimeOptions(string RuntimeUrl, string? Sha256)
{
    public static CudaReviewRuntimeOptions FromEnvironment()
    {
        return new CudaReviewRuntimeOptions(
            Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RuntimeUrlEnvironmentVariable) ??
                CudaReviewRuntimeService.DefaultRuntimeUrl,
            Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RuntimeSha256EnvironmentVariable));
    }
}

public sealed record CudaReviewRuntimeInstallResult(
    bool IsSucceeded,
    string Message,
    string InstallPath,
    string FailureCategory,
    string? Sha256 = null)
{
    public static CudaReviewRuntimeInstallResult Succeeded(string message, string installPath, string? sha256 = null)
    {
        return new CudaReviewRuntimeInstallResult(true, message, installPath, string.Empty, sha256);
    }

    public static CudaReviewRuntimeInstallResult Failed(string message, string installPath, string failureCategory, string? sha256 = null)
    {
        return new CudaReviewRuntimeInstallResult(false, message, installPath, failureCategory, sha256);
    }
}
