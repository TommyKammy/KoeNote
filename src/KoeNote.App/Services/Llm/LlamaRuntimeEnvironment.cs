using System.IO;

namespace KoeNote.App.Services.Llm;

public static class LlamaRuntimeEnvironment
{
    public const string CudaReviewRuntimeDirectoryVariable = "KOENOTE_CUDA_REVIEW_RUNTIME_DIR";

    public static IReadOnlyDictionary<string, string>? Build(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var runtimeDirectories = new[]
        {
            paths.CudaReviewRuntimeDirectory
        }.Where(Directory.Exists)
            .ToArray();
        if (runtimeDirectories.Length == 0)
        {
            return null;
        }

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
