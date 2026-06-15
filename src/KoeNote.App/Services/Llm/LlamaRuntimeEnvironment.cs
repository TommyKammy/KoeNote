using System.IO;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Llm;

public static class LlamaRuntimeEnvironment
{
    public const string CudaReviewRuntimeDirectoryVariable = "KOENOTE_CUDA_REVIEW_RUNTIME_DIR";

    public static IReadOnlyDictionary<string, string>? Build(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (!CudaReviewRuntimeLayout.HasPackage(paths))
        {
            return null;
        }

        var runtimeDirectories = new[]
        {
            paths.CudaReviewRuntimeDirectory
        };

        var existingPathEntries = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var path = string.Join(
            Path.PathSeparator,
            runtimeDirectories
                .Concat(existingPathEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = path,
            [CudaReviewRuntimeDirectoryVariable] = paths.CudaReviewRuntimeDirectory
        };
    }
}
