using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
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

    private static readonly RedistComponent[] CudaComponents =
    [
        new("cuda_cudart", "windows-x86_64", null, ["cudart64_*.dll"]),
        new("libcublas", "windows-x86_64", null, ["cublas64_*.dll", "cublasLt64_*.dll"])
    ];

    private static readonly RedistComponent[] CudnnComponents =
    [
        new("cudnn", "windows-x86_64", "cuda12", ["cudnn*.dll"])
    ];

    private readonly AsrCudaRuntimeOptions _options = options ?? AsrCudaRuntimeOptions.FromEnvironment();

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
            return AsrCudaRuntimeInstallResult.Succeeded("CUDA ASR runtime is already installed.", paths.AsrRuntimeDirectory);
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

            return await InstallLegacyRuntimeAsync(cancellationToken);
        }

        if (AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(paths.AsrRuntimeDirectory))
        {
            var marker = "nvidia-redist:existing";
            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, marker);
            return IsInstalled()
                ? AsrCudaRuntimeInstallResult.Succeeded("CUDA ASR runtime is already installed.", paths.AsrRuntimeDirectory, marker)
                : AsrCudaRuntimeInstallResult.Failed(
                    "CUDA ASR runtime files were present, but installation verification failed.",
                    paths.AsrRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    marker);
        }

        if (string.IsNullOrWhiteSpace(_options.CudaRedistManifestUrl) ||
            string.IsNullOrWhiteSpace(_options.CudaRedistBaseUrl) ||
            string.IsNullOrWhiteSpace(_options.CudnnRedistManifestUrl) ||
            string.IsNullOrWhiteSpace(_options.CudnnRedistBaseUrl))
        {
            return AsrCudaRuntimeInstallResult.Failed(
                $"NVIDIA redistributable source is not configured. Set {CudaRedistManifestUrlEnvironmentVariable}, {CudaRedistBaseUrlEnvironmentVariable}, {CudnnRedistManifestUrlEnvironmentVariable}, and {CudnnRedistBaseUrlEnvironmentVariable} before installing.",
                paths.AsrRuntimeDirectory,
                FailureCategoryConfigurationMissing);
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
            Directory.CreateDirectory(backupRoot);
            Report(progress, "インストール中", "CUDA ASR runtime の NVIDIA DLL を tools\\asr に配置しています...");
            CopyStagedFiles(stagingRoot, paths.AsrRuntimeDirectory, backupRoot, copiedFiles);

            Report(progress, "検証中", "CUDA ASR runtime の導入結果を検証しています...");
            var marker = $"nvidia-redist:{stagedResult.Source};{stagedResult.Sha256}";
            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, marker);
            return IsInstalled()
                ? AsrCudaRuntimeInstallResult.Succeeded($"CUDA ASR runtime installed: {paths.AsrRuntimeDirectory}", paths.AsrRuntimeDirectory, stagedResult.Sha256)
                : AsrCudaRuntimeInstallResult.Failed(
                    "CUDA ASR runtime files were copied, but installation verification failed.",
                    paths.AsrRuntimeDirectory,
                    FailureCategoryInstallFailed,
                    stagedResult.Sha256);
        }
        catch (AsrCudaRuntimeInstallException exception) when (exception.FailureCategory is FailureCategoryHashMismatch or FailureCategoryArchiveInvalid)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed(exception.Message, paths.AsrRuntimeDirectory, exception.FailureCategory, exception.Sha256);
        }
        catch (HttpRequestException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"NVIDIA redistributable download failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"NVIDIA redistributable archive could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (JsonException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"NVIDIA redistributable manifest could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            DeleteDirectoryIfExists(stagingRoot);
            DeleteDirectoryIfExists(backupRoot);
        }
    }

    private async Task<AsrCudaRuntimeInstallResult> InstallLegacyRuntimeAsync(CancellationToken cancellationToken)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-{Guid.NewGuid():N}");
        var backupRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-backup-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();

        try
        {
            await DownloadAsync(_options.LegacyRuntimeUrl!, tempPath, cancellationToken);
            var actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
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
                ExtractEntries(cudaEntries, stagingRoot);
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
            Directory.CreateDirectory(backupRoot);
            CopyStagedFiles(stagingRoot, paths.AsrRuntimeDirectory, backupRoot, copiedFiles);

            File.WriteAllText(paths.AsrCudaRuntimeMarkerPath, actualSha256);
            return IsInstalled()
                ? AsrCudaRuntimeInstallResult.Succeeded($"CUDA ASR runtime installed: {paths.AsrRuntimeDirectory}", paths.AsrRuntimeDirectory, actualSha256)
                : AsrCudaRuntimeInstallResult.Failed(
                    "CUDA ASR runtime files were copied, but installation verification failed.",
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
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime archive could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            RollBack(copiedFiles, backupRoot);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            DeleteIfExists(tempPath);
            DeleteDirectoryIfExists(stagingRoot);
            DeleteDirectoryIfExists(backupRoot);
        }
    }

    private async Task<StageResult> StageFromRedistsAsync(
        string stagingRoot,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        var cudaManifestSha = await StageComponentsAsync(
            _options.CudaRedistManifestUrl,
            _options.CudaRedistBaseUrl,
            CudaComponents,
            stagingRoot,
            cancellationToken,
            progress);
        var cudnnManifestSha = await StageComponentsAsync(
            _options.CudnnRedistManifestUrl,
            _options.CudnnRedistBaseUrl,
            CudnnComponents,
            stagingRoot,
            cancellationToken,
            progress);

        if (!AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(stagingRoot))
        {
            throw new AsrCudaRuntimeInstallException(
                "NVIDIA redistributable packages did not contain the required CUDA ASR DLLs.",
                FailureCategoryArchiveInvalid,
                $"{cudaManifestSha};{cudnnManifestSha}");
        }

        return new StageResult("download", $"{cudaManifestSha};{cudnnManifestSha}");
    }

    private async Task<string> StageComponentsAsync(
        string manifestUrl,
        string baseUrl,
        IReadOnlyCollection<RedistComponent> components,
        string stagingRoot,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        var manifestPath = Path.Combine(stagingRoot, $"redist-{Guid.NewGuid():N}.json");
        Report(progress, "ダウンロード中", "NVIDIA redist manifest を取得しています...");
        await DownloadAsync(manifestUrl, manifestPath, cancellationToken);
        Report(progress, "検証中", "NVIDIA redist manifest を検証しています...");
        var manifestSha256 = await ComputeSha256Async(manifestPath, cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        foreach (var component in components)
        {
            var package = ResolvePackage(document.RootElement, component);
            var packagePath = Path.Combine(stagingRoot, $"{component.Name}-{Guid.NewGuid():N}.zip");
            Report(progress, "ダウンロード中", $"NVIDIA {component.Name} redist を取得しています...");
            await DownloadAsync(ResolvePackageUrl(baseUrl, package.RelativePath), packagePath, cancellationToken);
            Report(progress, "検証中", $"NVIDIA {component.Name} redist の sha256 を検証しています...");
            var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken);
            if (!actualSha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new AsrCudaRuntimeInstallException(
                    $"NVIDIA redistributable SHA256 mismatch for {component.Name}. Expected {package.Sha256}, got {actualSha256}.",
                    FailureCategoryHashMismatch,
                    actualSha256);
            }

            Report(progress, "展開中", $"NVIDIA {component.Name} redist から必要な DLL を展開しています...");
            ExtractRequiredComponentFiles(packagePath, stagingRoot, component);
        }

        return manifestSha256;
    }

    private StageResult? TryStageFromLocalInstall(string stagingRoot)
    {
        var matches = TryFindRequiredNvidiaFiles(EnumerateLocalSearchRoots());
        if (matches is null)
        {
            return null;
        }

        foreach (var file in matches)
        {
            File.Copy(file, Path.Combine(stagingRoot, Path.GetFileName(file)), overwrite: true);
        }

        return new StageResult("local", "local");
    }

    private IEnumerable<string> EnumerateLocalSearchRoots()
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
        foreach (var root in new[]
                 {
                     Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA"),
                     Path.Combine(programFiles, "NVIDIA", "CUDNN")
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var versionDirectory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
                         .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return Path.Combine(versionDirectory, "bin");
            }
        }
    }

    private static IReadOnlyList<string>? TryFindRequiredNvidiaFiles(IEnumerable<string> roots)
    {
        var availableFiles = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
            .ToList();
        var matches = new List<string>();
        foreach (var pattern in AsrCudaRuntimeLayout.RequiredNvidiaFilePatterns)
        {
            var match = availableFiles.FirstOrDefault(file => MatchesPattern(Path.GetFileName(file), pattern));
            if (match is null)
            {
                return null;
            }

            matches.Add(match);
        }

        return matches;
    }

    private static RedistPackage ResolvePackage(JsonElement root, RedistComponent component)
    {
        if (!root.TryGetProperty(component.Name, out var componentElement) ||
            !componentElement.TryGetProperty(component.Platform, out var platformElement))
        {
            throw new AsrCudaRuntimeInstallException(
                $"NVIDIA redistributable manifest did not contain {component.Name} for {component.Platform}.",
                FailureCategoryArchiveInvalid);
        }

        var packageElement = platformElement;
        if (!string.IsNullOrWhiteSpace(component.Variant))
        {
            if (!platformElement.TryGetProperty(component.Variant, out packageElement))
            {
                throw new AsrCudaRuntimeInstallException(
                    $"NVIDIA redistributable manifest did not contain {component.Name} variant {component.Variant}.",
                    FailureCategoryArchiveInvalid);
            }
        }

        if (!packageElement.TryGetProperty("relative_path", out var relativePathElement) ||
            !packageElement.TryGetProperty("sha256", out var sha256Element))
        {
            throw new AsrCudaRuntimeInstallException(
                $"NVIDIA redistributable manifest entry for {component.Name} is missing relative_path or sha256.",
                FailureCategoryArchiveInvalid);
        }

        var relativePath = relativePathElement.GetString();
        var sha256 = sha256Element.GetString();
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(sha256))
        {
            throw new AsrCudaRuntimeInstallException(
                $"NVIDIA redistributable manifest entry for {component.Name} is missing relative_path or sha256.",
                FailureCategoryArchiveInvalid);
        }

        return new RedistPackage(relativePath, sha256);
    }

    private static string ResolvePackageUrl(string baseUrl, string relativePath)
    {
        var baseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/");
        return new Uri(baseUri, relativePath).ToString();
    }

    private static void ExtractRequiredComponentFiles(string packagePath, string stagingRoot, RedistComponent component)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                component.FilePatterns.Any(pattern => MatchesPattern(entry.Name, pattern)))
            .ToList();
        if (entries.Count == 0)
        {
            throw new AsrCudaRuntimeInstallException(
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

    private static IEnumerable<ZipArchiveEntry> GetLegacyEntries(ZipArchive archive)
    {
        return archive.Entries.Where(entry =>
            !string.IsNullOrWhiteSpace(entry.Name) &&
            AsrCudaRuntimeLayout.RequiredFilePatterns.Any(pattern => MatchesPattern(entry.Name, pattern)));
    }

    private static void ExtractEntries(IReadOnlyCollection<ZipArchiveEntry> entries, string stagingRoot)
    {
        var root = Path.GetFullPath(stagingRoot);
        foreach (var entry in entries)
        {
            var destinationPath = Path.GetFullPath(Path.Combine(root, Path.GetFileName(entry.FullName)));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry escapes the install directory: {entry.FullName}");
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void CopyStagedFiles(string stagingRoot, string destinationRoot, string backupRoot, ICollection<string> copiedFiles)
    {
        foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(static file => AsrCudaRuntimeLayout.RequiredFilePatterns.Any(pattern =>
                         MatchesPattern(Path.GetFileName(file), pattern))))
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

    private static void RollBack(IReadOnlyCollection<string> copiedFiles, string backupRoot)
    {
        foreach (var copiedFile in copiedFiles)
        {
            var backupPath = Path.Combine(backupRoot, Path.GetFileName(copiedFile));
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, copiedFile, overwrite: true);
            }
            else
            {
                DeleteIfExists(copiedFile);
            }
        }
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

    private sealed record RedistComponent(string Name, string Platform, string? Variant, IReadOnlyList<string> FilePatterns);

    private sealed record RedistPackage(string RelativePath, string Sha256);

    private sealed record StageResult(string Source, string Sha256);

    private sealed class AsrCudaRuntimeInstallException(string message, string failureCategory, string? sha256 = null) : Exception(message)
    {
        public string FailureCategory { get; } = failureCategory;

        public string? Sha256 { get; } = sha256;
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
        return new AsrCudaRuntimeOptions(
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistManifestUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudaRedistManifestUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistBaseUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudaRedistBaseUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistManifestUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudnnRedistManifestUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistBaseUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultCudnnRedistBaseUrl,
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable),
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeSha256EnvironmentVariable));
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
