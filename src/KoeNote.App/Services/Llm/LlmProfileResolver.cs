using System.IO;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Llm;

public sealed class LlmProfileResolver(
    AppPaths paths,
    InstalledModelRepository installedModelRepository)
{
    public const string FallbackReviewModelId = "llm-jp-4-8b-thinking-q4-k-m";

    public LlmRuntimeProfile Resolve(ModelCatalog catalog, string modelId)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("Model id is required.", nameof(modelId));
        }

        var catalogItem = catalog.Models.FirstOrDefault(model =>
            model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
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
            ModelPath: ResolveModelPath(modelId),
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

    private string ResolveModelPath(string modelId)
    {
        if (FindUsableInstalledReviewModel(modelId) is { } selectedPath)
        {
            return selectedPath;
        }

        if (!modelId.Equals(FallbackReviewModelId, StringComparison.OrdinalIgnoreCase) &&
            FindUsableInstalledReviewModel(FallbackReviewModelId) is { } fallbackPath)
        {
            return fallbackPath;
        }

        return paths.ReviewModelPath;
    }

    private string? FindUsableInstalledReviewModel(string modelId)
    {
        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is not null &&
            installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
        {
            return installed.FilePath;
        }

        return null;
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
