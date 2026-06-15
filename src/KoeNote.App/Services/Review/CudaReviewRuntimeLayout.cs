using System.IO;

namespace KoeNote.App.Services.Review;

public static class CudaReviewRuntimeLayout
{
    public static readonly string[] RequiredFilePatterns =
    [
        "ggml-cuda*.dll",
        "cublas64_*.dll",
        "cublasLt64_*.dll",
        "cudart64_*.dll"
    ];

    public static readonly string[] RequiredNvidiaFilePatterns =
    [
        "cublas64_*.dll",
        "cublasLt64_*.dll",
        "cudart64_*.dll"
    ];

    public static readonly string[] OptionalNvidiaFilePatterns =
    [
        "cufft*.dll",
        "curand*.dll",
        "cusparse*.dll"
    ];

    public static readonly string[] NvidiaFilePatterns =
    [
        .. RequiredNvidiaFilePatterns,
        .. OptionalNvidiaFilePatterns
    ];

    public static bool HasPackage(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return File.Exists(paths.LlamaCompletionPath) &&
            File.Exists(paths.CudaReviewRuntimeMarkerPath) &&
            Directory.Exists(paths.CudaReviewRuntimeDirectory) &&
            HasCudaBridge(paths) &&
            !HasNvidiaDependencies(paths.ReviewRuntimeDirectory) &&
            RequiredNvidiaFilePatterns.All(pattern =>
                Directory.EnumerateFiles(paths.CudaReviewRuntimeDirectory, pattern, SearchOption.TopDirectoryOnly).Any());
    }

    public static IReadOnlyList<string> GetMissingPackageItems(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        List<string> items = [];
        if (!File.Exists(paths.LlamaCompletionPath))
        {
            items.Add($"CPU review runtime: {paths.LlamaCompletionPath}");
        }

        if (!File.Exists(paths.CudaReviewRuntimeMarkerPath))
        {
            items.Add($"marker: {paths.CudaReviewRuntimeMarkerPath}");
        }

        if (!HasCudaBridge(paths))
        {
            items.Add($"KoeNote GPU bridge: ggml-cuda*.dll under {paths.ReviewRuntimeDirectory}");
        }

        if (HasNvidiaDependencies(paths.ReviewRuntimeDirectory))
        {
            items.Add($"legacy app-local NVIDIA DLLs must be migrated or removed from {paths.ReviewRuntimeDirectory}");
        }

        AddMissingFiles(items, "NVIDIA review runtime", paths.CudaReviewRuntimeDirectory, RequiredNvidiaFilePatterns);
        return items;
    }

    public static bool HasLegacyNvidiaRuntimeFiles(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return HasNvidiaDependencies(paths.ReviewRuntimeDirectory);
    }

    private static bool HasCudaBridge(AppPaths paths)
    {
        return HasCudaBridge(paths.ReviewRuntimeDirectory) || HasCudaBridge(paths.CudaReviewRuntimeDirectory);
    }

    private static bool HasCudaBridge(string directory)
    {
        return Directory.Exists(directory) &&
            Directory.EnumerateFiles(directory, "ggml-cuda*.dll", SearchOption.TopDirectoryOnly).Any();
    }

    private static bool HasNvidiaDependencies(string directory)
    {
        return Directory.Exists(directory) &&
            NvidiaFilePatterns.Any(pattern =>
                Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly).Any());
    }

    private static void AddMissingFiles(List<string> items, string label, string directory, IReadOnlyCollection<string> patterns)
    {
        var names = Directory.Exists(directory)
            ? Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
                .Cast<string>()
                .ToArray()
            : [];
        foreach (var pattern in patterns.Where(pattern => !names.Any(fileName =>
                     System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))))
        {
            items.Add($"{label}: {pattern} missing under {directory}");
        }
    }
}
