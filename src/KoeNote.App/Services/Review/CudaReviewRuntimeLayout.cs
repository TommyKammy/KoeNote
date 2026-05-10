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

    public static bool HasPackage(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return File.Exists(paths.LlamaCompletionPath) &&
            File.Exists(paths.CudaReviewRuntimeMarkerPath) &&
            Directory.Exists(paths.ReviewRuntimeDirectory) &&
            RequiredFilePatterns.All(pattern =>
                Directory.EnumerateFiles(paths.ReviewRuntimeDirectory, pattern, SearchOption.TopDirectoryOnly).Any());
    }
}
