using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Review;

public static class ReviewRuntimeResolver
{
    public const string TernaryRuntimePackageId = "runtime-llama-cpp-ternary";

    public static string ResolveLlamaCompletionPath(AppPaths paths, ModelCatalog catalog, string modelId)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(catalog);

        var packageId = catalog.Models
            .FirstOrDefault(model => model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ?.Runtime.PackageId;

        return string.Equals(packageId, TernaryRuntimePackageId, StringComparison.OrdinalIgnoreCase)
            ? paths.TernaryLlamaCompletionPath
            : paths.LlamaCompletionPath;
    }
}
