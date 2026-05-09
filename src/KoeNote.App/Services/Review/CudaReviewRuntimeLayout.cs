using System.IO;

namespace KoeNote.App.Services.Review;

public static class CudaReviewRuntimeLayout
{
    public static bool HasPackage(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return File.Exists(paths.LlamaCompletionPath) &&
            File.Exists(paths.CudaReviewRuntimeMarkerPath) &&
            Directory.EnumerateFiles(paths.ReviewRuntimeDirectory, "ggml-cuda*.dll").Any();
    }
}
