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
}
