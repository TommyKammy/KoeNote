using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
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
        new("cuda_cudart", ["cudart64_*.dll"]),
        new("libcublas", ["cublas64_*.dll", "cublasLt64_*.dll"])
    ];

    private readonly CudaReviewRuntimeOptions _options = options ?? CudaReviewRuntimeOptions.FromEnvironment();

    public bool IsInstalled()
    {
        return CudaReviewRuntimeLayout.HasPackage(paths);
    }

    public async Task<CudaReviewRuntimeInstallResult> InstallAsync(
        CancellationToken cancellationToken = default,
        IProgress<RuntimeInstallProgress>? progress = null)
    {
        Report(progress, "確認中", "CUDA review runtime の同梱ファイルと NVIDIA DLL を確認しています...");
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

        if (!HasBundledCudaBridge(paths.ReviewRuntimeDirectory))
        {
            if (string.IsNullOrWhiteSpace(_options.LegacyRuntimeUrl))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    "KoeNote GPU review bridge file ggml-cuda*.dll is not bundled. Ship it in tools\\review or configure the legacy runtime zip explicitly.",
                    paths.ReviewRuntimeDirectory,
                    FailureCategoryBundledRuntimeMissing);
            }

            return await InstallLegacyRuntimeAsync(cancellationToken);
        }

        if (HasRequiredNvidiaDependencies(paths.ReviewRuntimeDirectory))
        {
            var localMarker = "nvidia-redist:existing";
            Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, localMarker);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded("CUDA review runtime is already installed.", paths.ReviewRuntimeDirectory, localMarker)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were present, but installation verification failed.",
                    paths.ReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    localMarker);
        }

        if (string.IsNullOrWhiteSpace(_options.RedistManifestUrl) ||
            string.IsNullOrWhiteSpace(_options.RedistBaseUrl))
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"NVIDIA redistributable source is not configured. Set {RedistManifestUrlEnvironmentVariable} and {RedistBaseUrlEnvironmentVariable} before installing.",
                paths.ReviewRuntimeDirectory,
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

            Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
            Directory.CreateDirectory(backupRoot);
            Report(progress, "インストール中", "CUDA review runtime の NVIDIA DLL を tools\\review に配置しています...");
            CopyStagedFiles(stagingRoot, paths.ReviewRuntimeDirectory, backupRoot, copiedFiles);

            Report(progress, "検証中", "CUDA review runtime の導入結果を検証しています...");
            var marker = $"nvidia-redist:{stagedResult.Source};{stagedResult.ManifestSha256}";
            File.WriteAllText(paths.CudaReviewRuntimeMarkerPath, marker);
            return IsInstalled()
                ? CudaReviewRuntimeInstallResult.Succeeded($"CUDA review runtime installed: {paths.ReviewRuntimeDirectory}", paths.ReviewRuntimeDirectory, stagedResult.ManifestSha256)
                : CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime files were copied, but installation verification failed.",
                    paths.ReviewRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    stagedResult.ManifestSha256);
        }
        catch (CudaReviewRuntimeInstallException exception) when (exception.FailureCategory is FailureCategoryHashMismatch or FailureCategoryArchiveInvalid)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed(exception.Message, paths.ReviewRuntimeDirectory, exception.FailureCategory, exception.Sha256);
        }
        catch (HttpRequestException exception)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"NVIDIA redistributable download failed: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"NVIDIA redistributable archive could not be read: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (JsonException exception)
        {
            await RollBackAsync(copiedFiles, backupRoot);
            return CudaReviewRuntimeInstallResult.Failed($"NVIDIA redistributable manifest could not be read: {exception.Message}", paths.ReviewRuntimeDirectory, FailureCategoryArchiveInvalid);
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
                paths.ReviewRuntimeDirectory,
                FailureCategoryConfigurationMissing);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-review-runtime-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();

        try
        {
            await DownloadAsync(_options.LegacyRuntimeUrl, tempPath, cancellationToken);
            var actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.LegacyRuntimeSha256) &&
                !actualSha256.Equals(_options.LegacyRuntimeSha256, StringComparison.OrdinalIgnoreCase))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    $"CUDA review runtime SHA256 mismatch. Expected {_options.LegacyRuntimeSha256}, got {actualSha256}.",
                    paths.ReviewRuntimeDirectory,
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
                        paths.ReviewRuntimeDirectory,
                        FailureCategoryArchiveInvalid,
                        actualSha256);
                }

                ExtractEntries(cudaEntries, stagingRoot);
            }

            if (!HasBundledCudaBridge(stagingRoot) || !HasRequiredNvidiaDependencies(stagingRoot))
            {
                return CudaReviewRuntimeInstallResult.Failed(
                    "CUDA review runtime archive did not contain the required KoeNote bridge and NVIDIA DLLs.",
                    paths.ReviewRuntimeDirectory,
                    FailureCategoryArchiveInvalid,
                    actualSha256);
            }

            Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
            Directory.CreateDirectory(backupRoot);
            CopyStagedFiles(stagingRoot, paths.ReviewRuntimeDirectory, backupRoot, copiedFiles);

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

    private async Task<NvidiaRedistStageResult> StageFromNvidiaRedistAsync(
        string stagingRoot,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        var manifestPath = Path.Combine(stagingRoot, "nvidia-redist-manifest.json");
        Report(progress, "ダウンロード中", "NVIDIA CUDA redist manifest を取得しています...");
        await DownloadAsync(_options.RedistManifestUrl, manifestPath, cancellationToken);
        Report(progress, "検証中", "NVIDIA CUDA redist manifest を検証しています...");
        var manifestSha256 = await ComputeSha256Async(manifestPath, cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        foreach (var component in ReviewComponents)
        {
            var package = ResolvePackage(document.RootElement, component);
            var packagePath = Path.Combine(stagingRoot, $"{component.Name}-{Guid.NewGuid():N}.zip");
            Report(progress, "ダウンロード中", $"NVIDIA {component.Name} redist を取得しています...");
            await DownloadAsync(ResolvePackageUrl(package.RelativePath), packagePath, cancellationToken);
            Report(progress, "検証中", $"NVIDIA {component.Name} redist の sha256 を検証しています...");
            var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken);
            if (!actualSha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new CudaReviewRuntimeInstallException(
                    $"NVIDIA redistributable SHA256 mismatch for {component.Name}. Expected {package.Sha256}, got {actualSha256}.",
                    FailureCategoryHashMismatch,
                    actualSha256);
            }

            Report(progress, "展開中", $"NVIDIA {component.Name} redist から必要な DLL を展開しています...");
            ExtractRequiredComponentFiles(packagePath, stagingRoot, component);
        }

        if (!HasRequiredNvidiaDependencies(stagingRoot))
        {
            throw new CudaReviewRuntimeInstallException(
                "NVIDIA redistributable packages did not contain the required CUDA review DLLs.",
                FailureCategoryArchiveInvalid,
                manifestSha256);
        }

        return new NvidiaRedistStageResult("download", manifestSha256);
    }

    private NvidiaRedistStageResult? TryStageFromLocalCudaInstall(string stagingRoot)
    {
        foreach (var root in EnumerateLocalCudaSearchRoots())
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var matches = TryFindRequiredFiles(root);
            if (matches is null)
            {
                continue;
            }

            foreach (var file in matches)
            {
                File.Copy(file, Path.Combine(stagingRoot, Path.GetFileName(file)), overwrite: true);
            }

            return new NvidiaRedistStageResult($"local:{root}", "local");
        }

        return null;
    }

    private IEnumerable<string> EnumerateLocalCudaSearchRoots()
    {
        if (_options.LocalSearchRoots is not null)
        {
            foreach (var root in _options.LocalSearchRoots)
            {
                if (!string.IsNullOrWhiteSpace(root))
                {
                    yield return root;
                }
            }
        }

        foreach (var variable in Environment.GetEnvironmentVariables().Keys.OfType<string>()
                     .Where(static key => key.Equals("CUDA_PATH", StringComparison.OrdinalIgnoreCase) ||
                         key.StartsWith("CUDA_PATH_V", StringComparison.OrdinalIgnoreCase)))
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return Path.Combine(value, "bin");
            }
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var toolkitRoot = Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA");
        if (Directory.Exists(toolkitRoot))
        {
            foreach (var versionDirectory in Directory.EnumerateDirectories(toolkitRoot, "v*", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(versionDirectory, "bin");
            }
        }
    }

    private static IReadOnlyList<string>? TryFindRequiredFiles(string root)
    {
        var files = Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly).ToList();
        var matches = new List<string>();
        foreach (var pattern in CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns)
        {
            var match = files.FirstOrDefault(file => MatchesPattern(Path.GetFileName(file), pattern));
            if (match is null)
            {
                return null;
            }

            matches.Add(match);
        }

        return matches;
    }

    private NvidiaRedistPackage ResolvePackage(JsonElement root, NvidiaRedistComponent component)
    {
        if (!root.TryGetProperty(component.Name, out var componentElement) ||
            !componentElement.TryGetProperty("windows-x86_64", out var platformElement))
        {
            throw new CudaReviewRuntimeInstallException(
                $"NVIDIA redistributable manifest did not contain {component.Name} for windows-x86_64.",
                FailureCategoryArchiveInvalid);
        }

        if (!platformElement.TryGetProperty("relative_path", out var relativePathElement) ||
            !platformElement.TryGetProperty("sha256", out var sha256Element))
        {
            throw new CudaReviewRuntimeInstallException(
                $"NVIDIA redistributable manifest entry for {component.Name} is missing relative_path or sha256.",
                FailureCategoryArchiveInvalid);
        }

        var relativePath = relativePathElement.GetString();
        var sha256 = sha256Element.GetString();
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(sha256))
        {
            throw new CudaReviewRuntimeInstallException(
                $"NVIDIA redistributable manifest entry for {component.Name} is missing relative_path or sha256.",
                FailureCategoryArchiveInvalid);
        }

        return new NvidiaRedistPackage(relativePath, sha256);
    }

    private string ResolvePackageUrl(string relativePath)
    {
        var baseUri = new Uri(_options.RedistBaseUrl.EndsWith("/", StringComparison.Ordinal) ? _options.RedistBaseUrl : $"{_options.RedistBaseUrl}/");
        return new Uri(baseUri, relativePath).ToString();
    }

    private static void ExtractRequiredComponentFiles(string packagePath, string stagingRoot, NvidiaRedistComponent component)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                component.FilePatterns.Any(pattern => MatchesPattern(entry.Name, pattern)))
            .ToList();
        if (entries.Count == 0)
        {
            throw new CudaReviewRuntimeInstallException(
                $"NVIDIA redistributable package {component.Name} did not contain the required DLLs.",
                FailureCategoryArchiveInvalid);
        }

        ExtractEntries(entries, stagingRoot);
    }

    private async Task DownloadAsync(string url, string tempPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    private static IEnumerable<ZipArchiveEntry> GetLegacyCudaEntries(ZipArchive archive)
    {
        var patterns = CudaReviewRuntimeLayout.RequiredFilePatterns.Concat(CudaReviewRuntimeLayout.OptionalNvidiaFilePatterns).ToArray();
        return archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            patterns.Any(pattern => MatchesPattern(entry.Name, pattern)));
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

    private static void CopyStagedFiles(string stagingRoot, string destinationRoot, string backupRoot, ICollection<string> copiedFiles)
    {
        foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var relativePath = Path.GetRelativePath(stagingRoot, stagedFile);
            var destinationPath = Path.Combine(destinationRoot, relativePath);
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
    }

    private static bool HasBundledCudaBridge(string directory)
    {
        return Directory.Exists(directory) &&
            Directory.EnumerateFiles(directory, "ggml-cuda*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool HasRequiredNvidiaDependencies(string directory)
    {
        return Directory.Exists(directory) &&
            CudaReviewRuntimeLayout.RequiredNvidiaFilePatterns.All(pattern =>
                Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
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

    private static void Report(IProgress<RuntimeInstallProgress>? progress, string stageText, string message)
    {
        progress?.Report(new RuntimeInstallProgress(stageText, message));
    }

    private sealed record NvidiaRedistComponent(string Name, IReadOnlyList<string> FilePatterns);

    private sealed record NvidiaRedistPackage(string RelativePath, string Sha256);

    private sealed record NvidiaRedistStageResult(string Source, string ManifestSha256);

    private sealed class CudaReviewRuntimeInstallException(string message, string failureCategory, string? sha256 = null) : Exception(message)
    {
        public string FailureCategory { get; } = failureCategory;

        public string? Sha256 { get; } = sha256;
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
