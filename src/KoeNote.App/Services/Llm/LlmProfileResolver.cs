using System.IO;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Llm;

public sealed class LlmProfileResolver(
    AppPaths paths,
    InstalledModelRepository installedModelRepository)
{
    public const string FallbackReviewModelId = ReviewModelSelectionResolver.DefaultReviewModelId;
    public const string LegacyReviewModelId = ReviewModelSelectionResolver.LegacyReviewModelId;

    public LlmRuntimeProfile Resolve(ModelCatalog catalog, string modelId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model id is required.", nameof(modelId));
        }

        var catalogItem = catalog.Models.FirstOrDefault(model =>
            model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (catalogItem is null)
        {
            modelId = ReviewModelSelectionResolver.Resolve(catalog, modelId, selectedPresetId: null);
            catalogItem = catalog.Models.FirstOrDefault(model =>
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        }

        var preset = LlmPresetCatalog.ResolveRuntimePreset(modelId, catalogItem?.Family);
        var catalogSanitizerProfile = catalogItem?.OutputSanitizerProfile;

        var runtimePackageId = catalogItem?.Runtime.PackageId ?? "runtime-llama-cpp";
        var profile = new LlmRuntimeProfile(
            ProfileId: $"builtin:{modelId}:{preset.PresetId}",
            ModelId: modelId,
            ModelFamily: catalogItem?.Family,
            DisplayName: catalogItem?.DisplayName ?? modelId,
            RuntimeKind: catalogItem?.Runtime.Type ?? "llama-cpp",
            RuntimePackageId: runtimePackageId,
            ModelPath: ResolveModelPath(catalogItem, modelId),
            LlamaCompletionPath: ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, modelId),
            ContextSize: preset.ContextSize,
            GpuLayers: preset.GpuLayers,
            Threads: preset.Threads,
            ThreadsBatch: preset.ThreadsBatch,
            NoConversation: preset.NoConversation,
            OutputSanitizerProfile: LlmOutputSanitizerProfiles.ForReviewModel(modelId, catalogSanitizerProfile),
            Timeout: preset.Timeout)
        {
            AccelerationMode = ResolveAccelerationMode(runtimePackageId, preset.GpuLayers)
        };
        return profile;
    }

    private string ResolveModelPath(ModelCatalogItem? catalogItem, string modelId)
    {
        if (FindUsableInstalledReviewModel(modelId) is { } selectedPath)
        {
            return selectedPath;
        }

        if (ShouldUseDefaultModelAsFallback(catalogItem, modelId) &&
            FindUsableInstalledReviewModel(FallbackReviewModelId) is { } fallbackPath)
        {
            return fallbackPath;
        }

        if (modelId.Equals(LegacyReviewModelId, StringComparison.OrdinalIgnoreCase) &&
            LegacyReviewModelPathExists())
        {
            return paths.ReviewModelPath;
        }

        if (catalogItem?.Role.Equals("review", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Path.Combine(
                paths.DefaultModelStorageRoot,
                catalogItem.Role,
                catalogItem.ModelId,
                ResolveDownloadFileName(catalogItem));
        }

        return modelId.Equals(LegacyReviewModelId, StringComparison.OrdinalIgnoreCase)
            ? paths.ReviewModelPath
            : Path.Combine(paths.DefaultModelStorageRoot, "review", modelId, $"{modelId}.gguf");
    }

    private static bool ShouldUseDefaultModelAsFallback(ModelCatalogItem? catalogItem, string modelId)
    {
        return !modelId.Equals(FallbackReviewModelId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(catalogItem?.Family, "gemma", StringComparison.OrdinalIgnoreCase);
    }

    private string? FindUsableInstalledReviewModel(string modelId)
    {
        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is not null &&
            installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
        {
            return installed.FilePath;
        }

        return null;
    }

    private bool LegacyReviewModelPathExists()
    {
        return File.Exists(paths.ReviewModelPath) || Directory.Exists(paths.ReviewModelPath);
    }

    private static string ResolveDownloadFileName(ModelCatalogItem catalogItem)
    {
        if (catalogItem.Download.Url is { Length: > 0 } url &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return $"{catalogItem.ModelId}.gguf";
    }

    private string ResolveAccelerationMode(string runtimePackageId, int gpuLayers)
    {
        if (string.Equals(runtimePackageId, ReviewRuntimeResolver.TernaryRuntimePackageId, StringComparison.OrdinalIgnoreCase) ||
            gpuLayers <= 0)
        {
            return "cpu";
        }

        return CudaReviewRuntimeLayout.HasPackage(paths) ? "cuda" : "cpu";
    }
}
