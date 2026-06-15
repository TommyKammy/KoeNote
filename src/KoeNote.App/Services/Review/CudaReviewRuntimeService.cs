using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace KoeNote.App.Services.Review;

public sealed class CudaReviewRuntimeService(AppPaths paths, HttpClient httpClient, CudaReviewRuntimeOptions? options = null)
{
    public const string RuntimeUrlEnvironmentVariable = "KOENOTE_CUDA_REVIEW_RUNTIME_URL";
    public const string RuntimeSha256EnvironmentVariable = "KOENOTE_CUDA_REVIEW_RUNTIME_SHA256";
    public const string RedistManifestUrlEnvironmentVariable = "KOENOTE_CUDA_REVIEW_REDIST_MANIFEST_URL";
    public const string RedistBaseUrlEnvironmentVariable = "KOENOTE_CUDA_REVIEW_REDIST_BASE_URL";
    public const string DefaultRedistManifestUrl = "https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.9.0.json";
    public const string DefaultRedistBaseUrl = "https://developer.download.nvidia.com/compute/cuda/redist/";
    public const string FailureCategoryConfigurationMissing = "ConfigurationMissing";
    public const string FailureCategoryCpuRuntimeMissing = "CpuRuntimeMissing";
    public const string FailureCategoryBundledRuntimeMissing = "BundledRuntimeMissing";
    public const string FailureCategoryNetworkUnavailable = "NetworkUnavailable";
    public const string FailureCategoryHashMismatch = "HashMismatch";
    public const string FailureCategoryArchiveInvalid = "ArchiveInvalid";
    public const string FailureCategoryInstallFailed = "InstallFailed";

    private static readonly NvidiaRedistComponent[] ReviewComponents =
    [
        new("cuda_cudart", "windows-x86_64", null, ["cudart64_*.dll"]),
        new("libcublas", "windows-x86_64", null, ["cublas64_*.dll", "cublasLt64_*.dll"])
    ];

    private readonly CudaReviewRuntimeOptions _options = options ?? CudaReviewRuntimeOptions.FromEnvironment();
    private readonly NvidiaRedistInstaller _redistInstaller = new(httpClient);

    public bool IsInstalled()
    {
        return CudaReviewRuntimeLayout.HasPackage(paths);
    }

    public async Task<CudaReviewRuntimeInstallResult> InstallAsync(
        CancellationToken cancellationToken = default,
        IProgress<RuntimeInstallProgress>? progress = null)
    {
        Report(progress, "確認中", "CUDA review runtime の同梱ファイルと NVIDIA DLL を確認しています...");
        if (TryRefreshBundledCudaBridge() is { } refreshFailure)
        {
            return refreshFailure;
        }

        if (IsInstalled())
        {
            return CudaReviewRuntimeInstallResult.Succeeded("CUDA review runtime is already installed.", paths.CudaReviewRuntimeDirectory);
        }

        if (!File.Exists(paths.LlamaCompletionPath))
        {
            return CudaReviewRuntimeInstallResult.Failed(
                "CPU review runtime must be installed before adding CUDA review runtime files.",
                paths.CudaReviewRuntimeDirectory,
                FailureCategoryCpuRuntimeMissing);
        }

        if (!HasCudaBridge(paths.CudaReviewRuntimeDirectory))
        {
            if (HasCudaBridge(paths.ReviewRuntimeDirectory))
            {
                if (TryCopyMatchingFiles(paths.ReviewRuntimeDirectory, paths.CudaReviewRuntimeDirectory, ["ggml-cuda*.dll"]) is { } copyFailure)
                {
                    return copyFailure;
                }
            }
            else if (string.IsNullOrWhiteSpace(_options.LegacyRuntimeUrl))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    "KoeNote GPU review bridge file ggml-cuda*.dll is not bundled. Ship it in tools\\review or configure the legacy runtime zip explicitly.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryBundledRuntimeMissing);
            }
            else
            {
                return await InstallLegacyRuntimeAsync(cancellationToken);
            }
        }

        if (HasRequiredNvidiaDependencies(paths.CudaReviewRuntimeDirectory))
        {
            var localMarker = "nvidia-redist:existing";
            Directory.CreateDirectory(paths.CudaReviewRuntimeDirectory);
            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, localMarker);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded("CUDA review runtime is already installed.", paths.CudaReviewRuntimeDirectory, localMarker)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were present, but installation verification failed.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    localMarker);
        }

        if (HasRequiredNvidiaDependencies(paths.ReviewRuntimeDirectory))
        {
            if (TryCopyMatchingFiles(paths.ReviewRuntimeDirectory, paths.CudaReviewRuntimeDirectory, CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns) is { } copyFailure)
            {
                return copyFailure;
            }

            var localMarker = "nvidia-redist:migrated-from-tools-review";
            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, localMarker);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded("CUDA review runtime migrated to persistent runtime storage.", paths.CudaReviewRuntimeDirectory, localMarker)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were present, but migration to persistent runtime storage failed.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    localMarker);
        }

        if (string.IsNullOrWhiteSpace(_options.RedistManifestUrl) ||
            string.IsNullOrWhiteSpace(_options.RedistBaseUrl))
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"NVIDIA redistributable source is not configured. Set {RedistManifestUrlEnvironmentVariable} and {RedistBaseUrlEnvironmentVariable} before installing.",
                paths.CudaReviewRuntimeDirectory,
                FailureCategoryConfigurationMissing);
        }

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-redist-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-redist-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            Report(progress, "確認中", "ローカル CUDA Toolkit に必要な NVIDIA DLL があるか確認しています...");
            var stagedResult = TryStageFromLocalCudaInstall(stagingRoot);
            if (stagedResult is null)
            {
                stagedResult = await StageFromNvidiaRedistAsync(stagingRoot, cancellationToken, progress);
            }

            Directory.CreateDirectory(paths.CudaReviewRuntimeDirectory);
            Directory.CreateDirectory(backupRoot);
            Report(progress, "インストール中", $"CUDA review runtime の NVIDIA DLL を {paths.CudaReviewRuntimeDirectory} に配置しています...");
            NvidiaRedistInstaller.CopyStagedFiles(stagingRoot, paths.CudaReviewRuntimeDirectory, backupRoot, copiedFiles, ["*.dll"]);

            Report(progress, "検証中", "CUDA review runtime の導入結果を検証しています...");
            var marker = $"nvidia-redist:{stagedResult.Source};{stagedResult.Sha256}";
            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, marker);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded($"CUDA review runtime installed: {paths.CudaReviewRuntimeDirectory}", paths.CudaReviewRuntimeDirectory, stagedResult.Sha256)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were copied, but installation verification failed.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    stagedResult.Sha256);
        }
        catch (NvidiaRedistInstallException exception) when (exception.FailureCategory is FailureCategoryHashMismatch or FailureCategoryArchiveInvalid)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed(exception.Message, paths.CudaReviewRuntimeDirectory, exception.FailureCategory, exception.Sha256);
        }
        catch (HttpRequestException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"NVIDIA redistributable download failed: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"NVIDIA redistributable archive could not be read: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (JsonException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"NVIDIA redistributable manifest could not be read: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime install failed: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime install failed: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            DeleteDirectoryIfExists(stagingRoot);
            DeleteDirectoryIfExists(backupRoot);
        }
    }

    private async Task<CudaReviewRuntimeInstallResult> InstallLegacyRuntimeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.LegacyRuntimeUrl))
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime URL is not configured. Set {RuntimeUrlEnvironmentVariable} only when using the legacy all-in-one runtime zip.",
                paths.CudaReviewRuntimeDirectory,
                FailureCategoryConfigurationMissing);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();

        try
        {
            await _redistInstaller.DownloadAsync(_options.LegacyRuntimeUrl, tempPath, cancellationToken);
            var actualSha256 = await NvidiaRedistInstaller.ComputeSha256Async(tempPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.LegacyRuntimeSha256) &&
                !actualSha256.Equals(_options.LegacyRuntimeSha256, StringComparison.OrdinalIgnoreCase))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    $"CUDA review runtime SHA256 mismatch. Expected {_options.LegacyRuntimeSha256}, got {actualSha256}.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryHashMismatch,
                    actualSha256);
            }

            Directory.CreateDirectory(stagingRoot);
            using (var archive = ZipFile.OpenRead(tempPath))
            {
                var cudaEntries = GetLegacyCudaEntries(archive).ToList();
                if (cudaEntries.Count == 0)
                {
                    return CudaReviewRuntimeInstallResult.Failed(
                        "CUDA review runtime archive did not contain CUDA runtime DLLs.",
                        paths.CudaReviewRuntimeDirectory,
                        FailureCategoryArchiveInvalid,
                        actualSha256);
                }

                NvidiaRedistInstaller.ExtractEntries(cudaEntries, stagingRoot);
            }

            if (!HasCudaBridge(stagingRoot) || !HasRequiredNvidiaDependencies(stagingRoot))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime archive did not contain the required KoeNote bridge and NVIDIA DLLs.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryArchiveInvalid,
                    actualSha256);
            }

            Directory.CreateDirectory(paths.CudaReviewRuntimeDirectory);
            Directory.CreateDirectory(backupRoot);
            NvidiaRedistInstaller.CopyStagedFiles(stagingRoot, paths.CudaReviewRuntimeDirectory, backupRoot, copiedFiles, ["*.dll"]);

            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, actualSha256);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded($"CUDA review runtime installed: {paths.CudaReviewRuntimeDirectory}", paths.CudaReviewRuntimeDirectory, actualSha256)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were copied, but installation verification failed.",
                    paths.CudaReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    actualSha256);
        }
        catch (HttpRequestException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime download failed: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime archive could not be read: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime install failed: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"CUDA review runtime install failed: {exception.Message}", paths.CudaReviewRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            DeleteIfExists(tempPath);
            DeleteDirectoryIfExists(stagingRoot);
            DeleteDirectoryIfExists(backupRoot);
        }
    }

    private Task<NvidiaRedistStageResult> StageFromNvidiaRedistAsync(
        string stagingRoot,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        return _redistInstaller.StageFromRedistsAsync(
            stagingRoot,
            [new NvidiaRedistSource(_options.RedistManifestUrl, _options.RedistBaseUrl, ReviewComponents)],
            CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns,
                "NVIDIA redistributable packages did not contain the required CUDA review DLLs.",
            FailureCategoryHashMismatch,
            FailureCategoryArchiveInvalid,
            cancellationToken,
            progress);
    }

    private NvidiaRedistStageResult? TryStageFromLocalCudaInstall(string stagingRoot)
    {
        return _redistInstaller.TryStageFromLocalInstall(
            stagingRoot,
            NvidiaRedistInstaller.EnumerateCudaToolkitSearchRoots(_options.LocalSearchRoots),
            CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns);
    }

    private static IEnumerable<ZipArchiveEntry> GetLegacyCudaEntries(ZipArchive archive)
    {
        var patterns = CudaReviewRuntimeLayout.RequiredFilePatterns.Concat(CudaReviewRuntimeLayout.OptionalNvidiaFilePatterns).ToArray();
        return archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            patterns.Any(pattern => MatchesPattern(entry.Name, pattern)));
    }

    private static bool HasCudaBridge(string directory)
    {
        return Directory.Exists(directory) &&
            Directory.EnumerateFiles(directory, "ggml-cuda*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private CudaReviewRuntimeInstallResult? TryRefreshBundledCudaBridge()
    {
        if (!HasCudaBridge(paths.ReviewRuntimeDirectory))
        {
            return null;
        }

        return TryCopyMatchingFiles(paths.ReviewRuntimeDirectory, paths.CudaReviewRuntimeDirectory, ["ggml-cuda*.dll"]);
    }

    private CudaReviewRuntimeInstallResult? TryCopyMatchingFiles(string sourceDirectory, string destinationDirectory, IReadOnlyCollection<string> patterns)
    {
        try
        {
            CopyMatchingFiles(sourceDirectory, destinationDirectory, patterns);
            return null;
        }
        catch (IOException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime migration failed: {exception.Message}",
                destinationDirectory,
                FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime migration failed: {exception.Message}",
                destinationDirectory,
                FailureCategoryInstallFailed);
        }
    }

    private static void CopyMatchingFiles(string sourceDirectory, string destinationDirectory, IReadOnlyCollection<string> patterns)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.TopDirectoryOnly)
                     .Where(file => patterns.Any(pattern => MatchesPattern(Path.GetFileName(file), pattern))))
        {
            File.Copy(sourcePath, Path.Combine(destinationDirectory, Path.GetFileName(sourcePath)), overwrite: true);
        }
    }

    private static bool HasRequiredNvidiaDependencies(string directory)
    {
        return Directory.Exists(directory) &&
            CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns.All(pattern =>
                Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        return NvidiaRedistInstaller.MatchesPattern(fileName, pattern);
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

    private static void Report(IProgress<RuntimeInstallProgress>? progress, string stageText, string message)
    {
        progress?.Report(new RuntimeInstallProgress(stageText, message));
    }

}

public sealed record CudaReviewRuntimeOptions(
    string RedistManifestUrl,
    string RedistBaseUrl,
    string? LegacyRuntimeUrl = null,
    string? LegacyRuntimeSha256 = null,
    IReadOnlyList<string>? LocalSearchRoots = null)
{
    public static CudaReviewRuntimeOptions FromEnvironment()
    {
        return new CudaReviewRuntimeOptions(
            Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RedistManifestUrlEnvironmentVariable) ??
                CudaReviewRuntimeService.DefaultRedistManifestUrl,
            Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RedistBaseUrlEnvironmentVariable) ??
                CudaReviewRuntimeService.DefaultRedistBaseUrl,
            Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RuntimeUrlEnvironmentVariable),
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
