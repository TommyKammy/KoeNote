using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace KoeNote.App.Services;

internal sealed class NvidiaRedistInstaller(HttpClient httpClient)
{
    public async Task<NvidiaRedistStageResult> StageFromRedistsAsync(
        string stagingRoot,
        IReadOnlyCollection<NvidiaRedistSource> sources,
        IReadOnlyCollection<string> requiredFilePatterns,
        string missingRequiredFilesMessage,
        string hashMismatchCategory,
        string archiveInvalidCategory,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        var manifestHashes = new List<string>();
        foreach (var source in sources)
        {
            manifestHashes.Add(await StageSourceAsync(
                source,
                stagingRoot,
                hashMismatchCategory,
                archiveInvalidCategory,
                cancellationToken,
                progress));
        }

        if (!HasRequiredFiles(stagingRoot, requiredFilePatterns))
        {
            throw new NvidiaRedistInstallException(
                missingRequiredFilesMessage,
                archiveInvalidCategory,
                string.Join(";", manifestHashes));
        }

        return new NvidiaRedistStageResult("download", string.Join(";", manifestHashes));
    }

    public NvidiaRedistStageResult? TryStageFromLocalInstall(
        string stagingRoot,
        IEnumerable<string> searchRoots,
        IReadOnlyCollection<string> requiredFilePatterns)
    {
        var matches = TryFindRequiredFiles(searchRoots, requiredFilePatterns);
        if (matches is null)
        {
            return null;
        }

        foreach (var file in matches)
        {
            File.Copy(file, Path.Combine(stagingRoot, Path.GetFileName(file)), overwrite: true);
        }

        return new NvidiaRedistStageResult("local", "local");
    }

    public static IEnumerable<string> EnumerateCudaToolkitSearchRoots(IReadOnlyList<string>? localSearchRoots)
    {
        if (localSearchRoots is not null)
        {
            foreach (var root in localSearchRoots)
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

        var toolkitRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA GPU Computing Toolkit",
            "CUDA");
        if (!Directory.Exists(toolkitRoot))
        {
            yield break;
        }

        foreach (var versionDirectory in Directory.EnumerateDirectories(toolkitRoot, "v*", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(versionDirectory, "bin");
        }
    }

    public static IEnumerable<string> EnumerateCudnnSearchRoots()
    {
        var cudnnRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "NVIDIA",
            "CUDNN");
        if (!Directory.Exists(cudnnRoot))
        {
            yield break;
        }

        foreach (var versionDirectory in Directory.EnumerateDirectories(cudnnRoot, "*", SearchOption.TopDirectoryOnly)
                     .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            yield return Path.Combine(versionDirectory, "bin");
        }
    }

    public static void CopyStagedFiles(
        string stagingRoot,
        string destinationRoot,
        string backupRoot,
        ICollection<string> copiedFiles,
        IReadOnlyCollection<string> filePatterns)
    {
        foreach (var stagedFile in Directory.EnumerateFiles(stagingRoot, "*", SearchOption.TopDirectoryOnly)
                     .Where(file => filePatterns.Any(pattern =>
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

    public static void RollBack(IReadOnlyCollection<string> copiedFiles, string backupRoot)
    {
        foreach (var copiedFile in copiedFiles)
        {
            var backupPath = Path.Combine(backupRoot, Path.GetFileName(copiedFile));
            if (File.Exists(backupPath))
            {
                File.Copy(backupPath, copiedFile, overwrite: true);
            }
            else if (File.Exists(copiedFile))
            {
                File.Delete(copiedFile);
            }
        }
    }

    public static void ExtractEntries(IReadOnlyCollection<ZipArchiveEntry> entries, string stagingRoot)
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

    public static bool MatchesPattern(string fileName, string pattern)
    {
        return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true);
    }

    public async Task DownloadAsync(string url, string tempPath, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(tempPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<string> StageSourceAsync(
        NvidiaRedistSource source,
        string stagingRoot,
        string hashMismatchCategory,
        string archiveInvalidCategory,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        var manifestPath = Path.Combine(stagingRoot, $"redist-{Guid.NewGuid():N}.json");
        Report(progress, "ダウンロード中", "NVIDIA redist manifest を取得しています...");
        await DownloadAsync(source.ManifestUrl, manifestPath, cancellationToken);
        Report(progress, "検証中", "NVIDIA redist manifest を検証しています...");
        var manifestSha256 = await ComputeSha256Async(manifestPath, cancellationToken);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
        foreach (var component in source.Components)
        {
            var package = ResolvePackage(document.RootElement, component, archiveInvalidCategory);
            var packagePath = Path.Combine(stagingRoot, $"{component.Name}-{Guid.NewGuid():N}.zip");
            Report(progress, "ダウンロード中", $"NVIDIA {component.Name} redist を取得しています...");
            await DownloadAsync(ResolvePackageUrl(source.BaseUrl, package.RelativePath), packagePath, cancellationToken);
            Report(progress, "検証中", $"NVIDIA {component.Name} redist の sha256 を検証しています...");
            var actualSha256 = await ComputeSha256Async(packagePath, cancellationToken);
            if (!actualSha256.Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new NvidiaRedistInstallException(
                    $"NVIDIA redistributable SHA256 mismatch for {component.Name}. Expected {package.Sha256}, got {actualSha256}.",
                    hashMismatchCategory,
                    actualSha256);
            }

            Report(progress, "展開中", $"NVIDIA {component.Name} redist から必要な DLL を展開しています...");
            ExtractRequiredComponentFiles(packagePath, stagingRoot, component, archiveInvalidCategory);
        }

        return manifestSha256;
    }

    private static IReadOnlyList<string>? TryFindRequiredFiles(
        IEnumerable<string> roots,
        IReadOnlyCollection<string> requiredFilePatterns)
    {
        var files = roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.dll", SearchOption.TopDirectoryOnly))
            .ToList();
        var matches = new List<string>();
        foreach (var pattern in requiredFilePatterns)
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

    private static bool HasRequiredFiles(string directory, IReadOnlyCollection<string> requiredFilePatterns)
    {
        return Directory.Exists(directory) &&
            requiredFilePatterns.All(pattern =>
                Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
    }

    private static NvidiaRedistPackage ResolvePackage(
        JsonElement root,
        NvidiaRedistComponent component,
        string archiveInvalidCategory)
    {
        if (!root.TryGetProperty(component.Name, out var componentElement) ||
            !componentElement.TryGetProperty(component.Platform, out var platformElement))
        {
            throw new NvidiaRedistInstallException(
                $"NVIDIA redistributable manifest did not contain {component.Name} for {component.Platform}.",
                archiveInvalidCategory);
        }

        var packageElement = platformElement;
        if (!string.IsNullOrWhiteSpace(component.Variant))
        {
            if (!platformElement.TryGetProperty(component.Variant, out packageElement))
            {
                throw new NvidiaRedistInstallException(
                    $"NVIDIA redistributable manifest did not contain {component.Name} variant {component.Variant}.",
                    archiveInvalidCategory);
            }
        }

        if (!packageElement.TryGetProperty("relative_path", out var relativePathElement) ||
            !packageElement.TryGetProperty("sha256", out var sha256Element))
        {
            throw new NvidiaRedistInstallException(
                $"NVIDIA redistributable manifest entry for {component.Name} is missing relative_path or sha256.",
                archiveInvalidCategory);
        }

        var relativePath = relativePathElement.GetString();
        var sha256 = sha256Element.GetString();
        if (string.IsNullOrWhiteSpace(relativePath) || string.IsNullOrWhiteSpace(sha256))
        {
            throw new NvidiaRedistInstallException(
                $"NVIDIA redistributable manifest entry for {component.Name} is missing relative_path or sha256.",
                archiveInvalidCategory);
        }

        return new NvidiaRedistPackage(relativePath, sha256);
    }

    private static string ResolvePackageUrl(string baseUrl, string relativePath)
    {
        var baseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : $"{baseUrl}/");
        return new Uri(baseUri, relativePath).ToString();
    }

    private static void ExtractRequiredComponentFiles(
        string packagePath,
        string stagingRoot,
        NvidiaRedistComponent component,
        string archiveInvalidCategory)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        var entries = archive.Entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Name) &&
                component.FilePatterns.Any(pattern => MatchesPattern(entry.Name, pattern)))
            .ToList();
        if (entries.Count == 0)
        {
            throw new NvidiaRedistInstallException(
                $"NVIDIA redistributable package {component.Name} did not contain the required DLLs.",
                archiveInvalidCategory);
        }

        ExtractEntries(entries, stagingRoot);
    }

    private static void Report(IProgress<RuntimeInstallProgress>? progress, string stageText, string message)
    {
        progress?.Report(new RuntimeInstallProgress(stageText, message));
    }
}

internal sealed record NvidiaRedistComponent(
    string Name,
    string Platform,
    string? Variant,
    IReadOnlyList<string> FilePatterns);

internal sealed record NvidiaRedistSource(
    string ManifestUrl,
    string BaseUrl,
    IReadOnlyList<NvidiaRedistComponent> Components);

internal sealed record NvidiaRedistStageResult(string Source, string Sha256);

internal sealed class NvidiaRedistInstallException(
    string message,
    string failureCategory,
    string? sha256 = null) : Exception(message)
{
    public string FailureCategory { get; } = failureCategory;

    public string? Sha256 { get; } = sha256;
}

internal sealed record NvidiaRedistPackage(string RelativePath, string Sha256);
