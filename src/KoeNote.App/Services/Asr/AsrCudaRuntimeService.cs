using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace KoeNote.App.Services.Asr;

public sealed class AsrCudaRuntimeService(AppPaths paths, HttpClient httpClient, AsrCudaRuntimeOptions? options = null)
{
    public const string RuntimeUrlEnvironmentVariable = "KOENOTE_CUDA_ASR_RUNTIME_URL";
    public const string RuntimeSha256EnvironmentVariable = "KOENOTE_CUDA_ASR_RUNTIME_SHA256";
    public const string CudaRedistManifestUrlEnvironmentVariable = "KOENOTE_CUDA_ASR_REDIST_MANIFEST_URL";
    public const string CudaRedistBaseUrlEnvironmentVariable = "KOENOTE_CUDA_ASR_REDIST_BASE_URL";
    public const string CudnnRedistManifestUrlEnvironmentVariable = "KOENOTE_CUDA_ASR_CUDNN_REDIST_MANIFEST_URL";
    public const string CudnnRedistBaseUrlEnvironmentVariable = "KOENOTE_CUDA_ASR_CUDNN_REDIST_BASE_URL";
    public const string DefaultCudaRedistManifestUrl = "https://developer.download.nvidia.com/compute/cuda/redist/redistrib_12.9.0.json";
    public const string DefaultCudaRedistBaseUrl = "https://developer.download.nvidia.com/compute/cuda/redist/";
    public const string DefaultCudnnRedistManifestUrl = "https://developer.download.nvidia.com/compute/cudnn/redist/redistrib_9.22.0.json";
    public const string DefaultCudnnRedistBaseUrl = "https://developer.download.nvidia.com/compute/cudnn/redist/";
    public const string FailureCategoryConfigurationMissing = "ConfigurationMissing";
    public const string FailureCategoryBundledRuntimeMissing = "BundledRuntimeMissing";
    public const string FailureCategoryNetworkUnavailable = "NetworkUnavailable";
    public const string FailureCategoryHashMismatch = "HashMismatch";
    public const string FailureCategoryArchiveInvalid = "ArchiveInvalid";
    public const string FailureCategoryInstallFailed = "InstallFailed";

    private static readonly NvidiaRedistComponent[] CudaComponents =
    [
        new("cuda_cudart", "windows-x86_64", null, ["cudart64_*.dll"]),
        new("libcublas", "windows-x86_64", null, ["cublas64_*.dll", "cublasLt64_*.dll"])
    ];

    private static readonly NvidiaRedistComponent[] CudnnComponents =
    [
        new("cudnn", "windows-x86_64", "cuda12", ["cudnn*.dll"])
    ];

    private readonly AsrCudaRuntimeOptions _options = options ?? AsrCudaRuntimeInstallProjection.ResolveOptionsFromEnvironment();
    private readonly NvidiaRedistInstaller _redistInstaller = new(httpClient);

    public bool IsInstalled()
    {
        return AsrCudaRuntimeLayout.HasPackage(paths);
    }

    public async Task<AsrCudaRuntimeInstallResult> InstallAsync(
        CancellationToken cancellationToken = default,
        IProgress<RuntimeInstallProgress>? progress = null)
    {
        Report(progress, "確認中", "CUDA ASR runtime の同梱ファイルと NVIDIA DLL を確認しています...");
        if (IsInstalled())
        {
            return AsrCudaRuntimeInstallResult.Succeeded("CUDA ASR runtime is already installed.", paths.AsrCTranslate2RuntimeDirectory);
        }

        if (!AsrCudaRuntimeLayout.HasBundledRuntimeFiles(paths.AsrRuntimeDirectory))
        {
            if (string.IsNullOrWhiteSpace(_options.LegacyRuntimeUrl))
            {
                return AsrCudaRuntimeInstallResult.Failed(
                    "KoeNote ASR GPU runtime files are not bundled. Ship crispasr*, whisper.dll, and ggml-cuda.dll in tools\\asr or configure the legacy runtime zip explicitly.",
                    paths.AsrRuntimeDirectory,
                    FailureCategoryBundledRuntimeMissing);
            }

            return await InstallLegacyRuntimeAsync(cancellationToken, progress);
        }

        if (AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(paths.AsrCTranslate2RuntimeDirectory))
        {
            var marker = "nvidia-redist:existing";
            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, marker);
            return AsrCudaRuntimeInstallProjection.VerifyInstallResult(
                IsInstalled(),
                "CUDA ASR runtime is already installed.",
                "CUDA ASR runtime files were present, but installation verification failed.",
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrRuntimeDirectory,
                FailureCategoryInstallFailed,
                marker);
        }

        var legacyCTranslate2RuntimeDirectory = Path.Combine(paths.RuntimeTools, "asr-ctranslate2-cuda");
        if (AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(legacyCTranslate2RuntimeDirectory))
        {
            try
            {
                MirrorNvidiaFilesToCTranslate2Runtime(legacyCTranslate2RuntimeDirectory, paths.AsrCTranslate2RuntimeDirectory);
            }
            catch (IOException exception)
            {
                return AsrCudaRuntimeInstallProjection.MigrationFailed(exception, paths.AsrCTranslate2RuntimeDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                return AsrCudaRuntimeInstallProjection.MigrationFailed(exception, paths.AsrCTranslate2RuntimeDirectory);
            }

            var marker = "nvidia-redist:migrated-from-tools-asr-ctranslate2-cuda";
            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, marker);
            return AsrCudaRuntimeInstallProjection.VerifyInstallResult(
                IsInstalled(),
                "CUDA ASR runtime migrated from legacy CTranslate2 CUDA directory.",
                "CUDA ASR runtime files were present in the legacy CTranslate2 CUDA directory, but migration failed.",
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrCTranslate2RuntimeDirectory,
                FailureCategoryInstallFailed,
                marker);
        }

        if (AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(paths.AsrRuntimeDirectory))
        {
            try
            {
                MirrorNvidiaFilesToCTranslate2Runtime(paths.AsrRuntimeDirectory, paths.AsrCTranslate2RuntimeDirectory, deleteSourceFiles: true);
            }
            catch (IOException exception)
            {
                return AsrCudaRuntimeInstallProjection.MigrationFailed(exception, paths.AsrRuntimeDirectory);
            }
            catch (UnauthorizedAccessException exception)
            {
                return AsrCudaRuntimeInstallProjection.MigrationFailed(exception, paths.AsrRuntimeDirectory);
            }

            var marker = "nvidia-redist:migrated-from-tools-asr";
            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, marker);
            return AsrCudaRuntimeInstallProjection.VerifyInstallResult(
                IsInstalled(),
                "CUDA ASR runtime migrated to dedicated CTranslate2 CUDA directory.",
                "CUDA ASR runtime files were present, but migration to the dedicated CTranslate2 CUDA directory failed.",
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrRuntimeDirectory,
                FailureCategoryInstallFailed,
                marker);
        }

        if (!AsrCudaRuntimeInstallProjection.HasNvidiaRedistSources(_options))
        {
            return AsrCudaRuntimeInstallProjection.MissingNvidiaRedistSources(paths);
        }

        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-redist-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-redist-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();
        try
        {
            Directory.CreateDirectory(stagingRoot);
            Report(progress, "確認中", "ローカル CUDA/cuDNN に必要な NVIDIA DLL があるか確認しています...");
            var stagedResult = TryStageFromLocalInstall(stagingRoot);
            if (stagedResult is null)
            {
                stagedResult = await StageFromRedistsAsync(stagingRoot, cancellationToken, progress);
            }

            Directory.CreateDirectory(paths.AsrRuntimeDirectory);
            Directory.CreateDirectory(paths.AsrCTranslate2RuntimeDirectory);
            Directory.CreateDirectory(backupRoot);
            Report(progress, "インストール中", $"CUDA ASR runtime の NVIDIA DLL を {paths.AsrCTranslate2RuntimeDirectory} に配置しています...");
            NvidiaRedistInstaller.CopyStagedFiles(stagingRoot, paths.AsrCTranslate2RuntimeDirectory, backupRoot, copiedFiles, AsrCudaRuntimeLayout.RequiredNvidiaFilePatterns);

            Report(progress, "検証中", "CUDA ASR runtime の導入結果を検証しています...");
            var marker = $"nvidia-redist:{stagedResult.Source};{stagedResult.Sha256}";
            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, marker);
            return AsrCudaRuntimeInstallProjection.VerifyInstallResult(
                IsInstalled(),
                $"CUDA ASR runtime installed: {paths.AsrCTranslate2RuntimeDirectory}",
                "CUDA ASR runtime files were copied, but installation verification failed.",
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrRuntimeDirectory,
                FailureCategoryInstallFailed,
                stagedResult.Sha256);
        }
        catch (NvidiaRedistInstallException exception) when (exception.FailureCategory is FailureCategoryHashMismatch or FailureCategoryArchiveInvalid)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed(exception.Message, paths.AsrRuntimeDirectory, exception.FailureCategory, exception.Sha256);
        }
        catch (HttpRequestException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"NVIDIA redistributable download failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"NVIDIA redistributable archive could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (JsonException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"NVIDIA redistributable manifest could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            RuntimeInstallFileOps.DeleteDirectoryIfExists(stagingRoot);
            RuntimeInstallFileOps.DeleteDirectoryIfExists(backupRoot);
        }
    }

    private async Task<AsrCudaRuntimeInstallResult> InstallLegacyRuntimeAsync(
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();

        try
        {
            await _redistInstaller.DownloadAsync(
                _options.LegacyRuntimeUrl!,
                tempPath,
                cancellationToken,
                progress,
                "ダウンロード中",
                "CUDA ASR runtime をダウンロードしています...");
            var actualSha256 = await NvidiaRedistInstaller.ComputeSha256Async(tempPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.LegacyRuntimeSha256) &&
                !actualSha256.Equals(_options.LegacyRuntimeSha256, StringComparison.OrdinalIgnoreCase))
            {
                return AsrCudaRuntimeInstallResult.Failed(
                    $"CUDA ASR runtime SHA256 mismatch. Expected {_options.LegacyRuntimeSha256}, got {actualSha256}.",
                    paths.AsrRuntimeDirectory,
                    FailureCategoryHashMismatch,
                    actualSha256);
            }

            Directory.CreateDirectory(stagingRoot);
            using (var archive = ZipFile.OpenRead(tempPath))
            {
                var cudaEntries = GetLegacyEntries(archive).ToList();
                NvidiaRedistInstaller.ExtractEntries(cudaEntries, stagingRoot);
            }

            if (!AsrCudaRuntimeLayout.HasRequiredFiles(Directory.EnumerateFiles(stagingRoot).Select(Path.GetFileName).Cast<string>()))
            {
                return AsrCudaRuntimeInstallResult.Failed(
                    "CUDA ASR runtime archive must contain KoeNote ASR GPU runtime files plus cuBLAS, CUDA runtime, and cuDNN DLLs.",
                    paths.AsrRuntimeDirectory,
                    FailureCategoryArchiveInvalid,
                    actualSha256);
            }

            Directory.CreateDirectory(paths.AsrRuntimeDirectory);
            Directory.CreateDirectory(paths.AsrCTranslate2RuntimeDirectory);
            Directory.CreateDirectory(backupRoot);
            NvidiaRedistInstaller.CopyStagedFiles(stagingRoot, paths.AsrRuntimeDirectory, backupRoot, copiedFiles, AsrCudaRuntimeLayout.RequiredBundledFilePatterns);
            NvidiaRedistInstaller.CopyStagedFiles(stagingRoot, paths.AsrCTranslate2RuntimeDirectory, backupRoot, copiedFiles, AsrCudaRuntimeLayout.RequiredNvidiaFilePatterns);

            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, actualSha256);
            return AsrCudaRuntimeInstallProjection.VerifyInstallResult(
                IsInstalled(),
                $"CUDA ASR runtime installed: {paths.AsrCTranslate2RuntimeDirectory}",
                "CUDA ASR runtime files were copied, but installation verification failed.",
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrRuntimeDirectory,
                FailureCategoryInstallFailed,
                actualSha256);
        }
        catch (HttpRequestException exception)
        {
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime download failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime archive could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            NvidiaRedistInstaller.RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            RuntimeInstallFileOps.DeleteFileIfExists(tempPath);
            RuntimeInstallFileOps.DeleteDirectoryIfExists(stagingRoot);
            RuntimeInstallFileOps.DeleteDirectoryIfExists(backupRoot);
        }
    }

    private Task<NvidiaRedistStageResult> StageFromRedistsAsync(
        string stagingRoot,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        return _redistInstaller.StageFromRedistsAsync(
            stagingRoot,
            [
                new NvidiaRedistSource(_options.CudaRedistManifestUrl, _options.CudaRedistBaseUrl, CudaComponents),
                new NvidiaRedistSource(_options.CudnnRedistManifestUrl, _options.CudnnRedistBaseUrl, CudnnComponents)
            ],
            AsrCudaRuntimeLayout.RequiredNvidiaFilePatterns,
                "NVIDIA redistributable packages did not contain the required CUDA ASR DLLs.",
            FailureCategoryHashMismatch,
            FailureCategoryArchiveInvalid,
            cancellationToken,
            progress);
    }

    private NvidiaRedistStageResult? TryStageFromLocalInstall(string stagingRoot)
    {
        return _redistInstaller.TryStageFromLocalInstall(
            stagingRoot,
            EnumerateLocalSearchRoots(),
            AsrCudaRuntimeLayout.RequiredNvidiaFilePatterns,
            allowMixedRoots: true);
    }

    private IEnumerable<string> EnumerateLocalSearchRoots()
    {
        foreach (var root in NvidiaRedistInstaller.EnumerateCudaToolkitSearchRoots(_options.LocalSearchRoots)
                     .Concat(NvidiaRedistInstaller.EnumerateCudnnSearchRoots()))
        {
            yield return root;
        }
    }

    private static IEnumerable<ZipArchiveEntry> GetLegacyEntries(ZipArchive archive)
    {
        return archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            AsrCudaRuntimeLayout.RequiredFilePatterns.Any(pattern =>
                RuntimeInstallFileOps.MatchesPattern(entry.Name, pattern)));
    }

    private static void MirrorNvidiaFilesToCTranslate2Runtime(string sourceRoot, string destinationRoot, bool deleteSourceFiles = false)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        RuntimeInstallFileOps.CopyMatchingFiles(
            sourceRoot,
            destinationRoot,
            AsrCudaRuntimeLayout.RequiredNvidiaFilePatterns,
            deleteSourceFiles,
            searchPattern: "*.dll");
    }

    private static void Report(IProgress<RuntimeInstallProgress>? progress, string stageText, string message)
    {
        progress?.Report(new RuntimeInstallProgress(stageText, message));
    }

}

public sealed record AsrCudaRuntimeOptions(
    string CudaRedistManifestUrl,
    string CudaRedistBaseUrl,
    string CudnnRedistManifestUrl,
    string CudnnRedistBaseUrl,
    string? LegacyRuntimeUrl = null,
    string? LegacyRuntimeSha256 = null,
    IReadOnlyList<string>? LocalSearchRoots = null)
{
    public static AsrCudaRuntimeOptions FromEnvironment()
    {
        return AsrCudaRuntimeInstallProjection.ResolveOptionsFromEnvironment();
    }
}

public sealed record AsrCudaRuntimeInstallResult(
    bool IsSucceeded,
    string Message,
    string InstallPath,
    string FailureCategory,
    string? Sha256 = null)
{
    public static AsrCudaRuntimeInstallResult Succeeded(string message, string installPath, string? sha256 = null)
    {
        return new AsrCudaRuntimeInstallResult(true, message, installPath, string.Empty, sha256);
    }

    public static AsrCudaRuntimeInstallResult Failed(string message, string installPath, string failureCategory, string? sha256 = null)
    {
        return new AsrCudaRuntimeInstallResult(false, message, installPath, failureCategory, sha256);
    }
}
