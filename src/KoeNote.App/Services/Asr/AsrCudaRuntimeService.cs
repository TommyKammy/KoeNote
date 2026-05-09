using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace KoeNote.App.Services.Asr;

public sealed class AsrCudaRuntimeService(AppPaths paths, HttpClient httpClient, AsrCudaRuntimeOptions? options = null)
{
    public const string RuntimeUrlEnvironmentVariable = "KOENOTE_CUDA_ASR_RUNTIME_URL";
    public const string RuntimeSha256EnvironmentVariable = "KOENOTE_CUDA_ASR_RUNTIME_SHA256";
    public const string DefaultRuntimeUrl = "https://github.com/TommyKammy/KoeNote/releases/latest/download/koenote-cuda-asr-runtime.zip";
    public const string FailureCategoryConfigurationMissing = "ConfigurationMissing";
    public const string FailureCategoryNetworkUnavailable = "NetworkUnavailable";
    public const string FailureCategoryHashMismatch = "HashMismatch";
    public const string FailureCategoryArchiveInvalid = "ArchiveInvalid";
    public const string FailureCategoryInstallFailed = "InstallFailed";

    private readonly AsrCudaRuntimeOptions _options = options ?? AsrCudaRuntimeOptions.FromEnvironment();

    public bool IsInstalled()
    {
        return AsrCudaRuntimeLayout.HasPackage(paths);
    }

    public async Task<AsrCudaRuntimeInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled())
        {
            return AsrCudaRuntimeInstallResult.Succeeded("CUDA ASR runtime is already installed.", paths.AsrRuntimeDirectory);
        }

        if (string.IsNullOrWhiteSpace(_options.RuntimeUrl))
        {
            return AsrCudaRuntimeInstallResult.Failed(
                $"CUDA ASR runtime URL is not configured. Set {RuntimeUrlEnvironmentVariable} before installing.",
                paths.AsrRuntimeDirectory,
                FailureCategoryConfigurationMissing);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-{Guid.NewGuid():N}.zip");
        var stagingRoot = Path.Combine(Path.GetTempPath(), $"koenote-cuda-asr-runtime-{Guid.NewGuid():N}");
        var copiedFiles = new List<string>();

        try
        {
            await DownloadAsync(tempPath, cancellationToken);
            var actualSha256 = await ComputeSha256Async(tempPath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(_options.Sha256) &&
                !actualSha256.Equals(_options.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return AsrCudaRuntimeInstallResult.Failed(
                    $"CUDA ASR runtime SHA256 mismatch. Expected {_options.Sha256}, got {actualSha256}.",
                    paths.AsrRuntimeDirectory,
                    FailureCategoryHashMismatch,
                    actualSha256);
            }

            Directory.CreateDirectory(stagingRoot);
            using (var archive = ZipFile.OpenRead(tempPath))
            {
                var cudaEntries = GetCudaEntries(archive).ToList();
                if (!AsrCudaRuntimeLayout.HasRequiredFiles(cudaEntries.Select(static entry => entry.Name)))
                {
                    return AsrCudaRuntimeInstallResult.Failed(
                        "CUDA ASR runtime archive must contain cuBLAS, cuBLASLt, CUDA runtime, cuDNN, and zlib DLLs.",
                        paths.AsrRuntimeDirectory,
                        FailureCategoryArchiveInvalid,
                        actualSha256);
                }

                ExtractEntries(cudaEntries, stagingRoot);
            }

            Directory.CreateDirectory(paths.AsrRuntimeDirectory);
            foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.AllDirectories))
            {
                var destinationPath = Path.Combine(paths.AsrRuntimeDirectory, Path.GetFileName(stagedFile));
                File.Copy(stagedFile, destinationPath, overwrite: true);
                copiedFiles.Add(destinationPath);
            }

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
            RollBack(copiedFiles);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime archive could not be read: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            RollBack(copiedFiles);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            RollBack(copiedFiles);
            return AsrCudaRuntimeInstallResult.Failed($"CUDA ASR runtime install failed: {exception.Message}", paths.AsrRuntimeDirectory, FailureCategoryInstallFailed);
        }
        finally
        {
            DeleteIfExists(tempPath);
            DeleteDirectoryIfExists(stagingRoot);
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
            AsrCudaRuntimeLayout.RequiredFilePatterns.Any(pattern =>
                System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, entry.Name, ignoreCase: true)));
    }

    private static void ExtractEntries(IReadOnlyCollection<ZipArchiveEntry> entries, string stagingRoot)
    {
        foreach (var entry in entries)
        {
            entry.ExtractToFile(Path.Combine(stagingRoot, entry.Name), overwrite: true);
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void RollBack(IReadOnlyCollection<string> copiedFiles)
    {
        foreach (var copiedFile in copiedFiles)
        {
            DeleteIfExists(copiedFile);
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
}

public sealed record AsrCudaRuntimeOptions(string RuntimeUrl, string? Sha256)
{
    public static AsrCudaRuntimeOptions FromEnvironment()
    {
        return new AsrCudaRuntimeOptions(
            Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable) ??
                AsrCudaRuntimeService.DefaultRuntimeUrl,
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
