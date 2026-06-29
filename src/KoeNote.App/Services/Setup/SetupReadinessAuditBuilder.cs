using System.IO;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupReadinessAuditBuilder(
    AppPaths paths,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository)
{
    public IReadOnlyList<SetupModelAudit> GetSelectedModelAudit(SetupState state)
    {
        return GetSelectedInstalledModels(state)
            .Select(model => new SetupModelAudit(
                model.ModelId,
                !string.IsNullOrWhiteSpace(model.Sha256),
                string.IsNullOrWhiteSpace(model.Sha256) ? "checksum not recorded" : model.Sha256,
                !string.IsNullOrWhiteSpace(model.ManifestPath) && File.Exists(model.ManifestPath),
                string.IsNullOrWhiteSpace(model.ManifestPath) ? "manifest not recorded" : model.ManifestPath,
                !string.IsNullOrWhiteSpace(model.LicenseName),
                string.IsNullOrWhiteSpace(model.LicenseName) ? "license not recorded" : model.LicenseName))
            .ToArray();
    }

    public IReadOnlyList<SetupExistingDataItem> GetExistingDataSummary()
    {
        var installedModels = File.Exists(paths.DatabasePath)
            ? installedModelRepository.ListInstalledModels()
                .Where(static model => File.Exists(model.FilePath) || Directory.Exists(model.FilePath))
                .ToArray()
            : [];
        var jobCount = Directory.Exists(paths.Jobs)
            ? Directory.EnumerateDirectories(paths.Jobs).Count()
            : 0;

        return
        [
            new("setup state", File.Exists(paths.SetupStatePath), paths.SetupStatePath),
            new("jobs database", File.Exists(paths.DatabasePath), paths.DatabasePath),
            new("job folders", jobCount > 0, $"{jobCount} folder(s) under {paths.Jobs}"),
            new("registered models", installedModels.Length > 0, $"{installedModels.Length} model(s) recorded in installed_models"),
            new("user model storage", Directory.Exists(paths.UserModels), paths.UserModels),
            new("machine model storage", Directory.Exists(paths.MachineModels), paths.MachineModels)
        ];
    }

    public bool IsSelectedModelReady(string? modelId, string role, bool requireRuntimeBridge = false)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var installed = installedModelRepository.FindInstalledModel(modelId);
        return installed is not null &&
            installed.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)) &&
            (!requireRuntimeBridge || LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath));
    }

    public bool IsSelectedDirectLlmFallbackReady(string? modelId)
    {
        return !RequiresDirectLlmStageFallback(modelId) ||
            IsSelectedModelReady(ReviewModelSelectionResolver.DefaultReviewModelId, "review", requireRuntimeBridge: true);
    }

    public bool IsSelectedReviewRuntimeReady(string? modelId)
    {
        return File.Exists(GetSelectedReviewRuntimePath(modelId)) &&
            (!RequiresGemma12BMtpAssets(modelId) ||
             File.Exists(Gemma12BLocalValidation.ResolveLlamaServerPath(paths.LlamaCompletionPath)));
    }

    public bool IsSelectedGemma12BMtpDraftReady(string? modelId, string? storageRoot = null)
    {
        if (!RequiresGemma12BMtpAssets(modelId))
        {
            return true;
        }

        return TryResolveReadyGemma12BMtpDraftPath(storageRoot, out _);
    }

    public bool IsSelectedGemma12BMtpRequirementMissing(SetupState state)
    {
        return RequiresGemma12BMtpAssets(state.SelectedReviewModelId) &&
            (!IsSelectedReviewRuntimeReady(state.SelectedReviewModelId) ||
             !IsSelectedGemma12BMtpDraftReady(state.SelectedReviewModelId, state.StorageRoot));
    }

    public bool IsSelectedGpuRequirementSatisfied(SetupState state, SetupHostResources resources)
    {
        if (resources.NvidiaGpuDetected)
        {
            return true;
        }

        return GetSelectedCatalogModels(state)
            .All(static model => !model.Requirements.GpuRequired);
    }

    public bool IsSelectedTernaryReviewRuntimeMissing(SetupState state)
    {
        if (string.IsNullOrWhiteSpace(state.SelectedReviewModelId))
        {
            return false;
        }

        var runtimePath = GetSelectedReviewRuntimePath(state.SelectedReviewModelId);
        return string.Equals(runtimePath, paths.TernaryLlamaCompletionPath, StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(runtimePath);
    }

    public SetupSmokeCheck CheckSelectedModel(string name, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return new SetupSmokeCheck(name, false, "Select a model in Setup.");
        }

        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is null)
        {
            return new SetupSmokeCheck(name, false, $"Not installed: {modelId}");
        }

        var exists = File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath);
        var verified = installed.Verified && exists;
        return new SetupSmokeCheck(name, verified, verified ? installed.FilePath : $"Verification failed or missing: {installed.FilePath}");
    }

    public SetupSmokeCheck CheckSelectedReviewRuntime(string? modelId)
    {
        var runtimePath = GetSelectedReviewRuntimePath(modelId);
        var exists = File.Exists(runtimePath);
        return new SetupSmokeCheck("Review LLM runtime", exists, exists ? runtimePath : $"Missing: {runtimePath}");
    }

    public IReadOnlyList<SetupSmokeCheck> CheckGemma12BMtpRequirements(string? modelId, string? storageRoot)
    {
        if (!RequiresGemma12BMtpAssets(modelId))
        {
            return [];
        }

        var llamaServerPath = Gemma12BLocalValidation.ResolveLlamaServerPath(paths.LlamaCompletionPath);
        var serverExists = File.Exists(llamaServerPath);
        var serverReady = serverExists && Gemma12BLocalValidation.IsLlamaServerMtpCapable(llamaServerPath);
        var draftExists = TryResolveReadyGemma12BMtpDraftPath(storageRoot, out var draftPath);
        draftPath ??= Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath() ??
            $"Not installed: {Gemma12BLocalValidation.MtpDraftModelId}";

        return
        [
            new SetupSmokeCheck(
                "Gemma 4 12B MTP server runtime",
                serverReady,
                serverReady
                    ? llamaServerPath
                    : serverExists
                        ? $"Unsupported llama-server MTP runtime: {llamaServerPath}"
                        : $"Missing: {llamaServerPath}"),
            new SetupSmokeCheck(
                "Gemma 4 12B MTP draft model",
                draftExists,
                draftPath)
        ];
    }

    public IReadOnlyList<SetupSmokeCheck> CheckDirectLlmFallbackRequirements(string? modelId)
    {
        if (!RequiresDirectLlmStageFallback(modelId))
        {
            return [];
        }

        var installed = installedModelRepository.FindInstalledModel(ReviewModelSelectionResolver.DefaultReviewModelId);
        if (installed is null)
        {
            return
            [
                new SetupSmokeCheck(
                    "Review/Summary fallback model",
                    false,
                    $"Not installed: {ReviewModelSelectionResolver.DefaultReviewModelId}")
            ];
        }

        var exists = File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath);
        var verified = installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            exists &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath);
        return
        [
            new SetupSmokeCheck(
                "Review/Summary fallback model",
                verified,
                verified ? installed.FilePath : $"Verification failed, missing, or runtime bridge unavailable: {installed.FilePath}")
        ];
    }

    public IReadOnlyList<SetupSmokeCheck> CheckSelectedGpuRequirements(SetupState state, SetupHostResources resources)
    {
        if (resources.NvidiaGpuDetected)
        {
            return [];
        }

        return GetSelectedCatalogModels(state)
            .Where(static model => model.Requirements.GpuRequired)
            .Select(static model => new SetupSmokeCheck(
                $"{model.DisplayName} GPU requirement",
                false,
                "This model requires an NVIDIA GPU. Select another model or use a GPU-equipped PC."))
            .ToArray();
    }

    public IReadOnlyList<InstalledModel> GetSelectedInstalledModels(SetupState state)
    {
        var modelIds = new List<string?>([state.SelectedAsrModelId, state.SelectedReviewModelId]);
        if (RequiresDirectLlmStageFallback(state.SelectedReviewModelId))
        {
            modelIds.Add(ReviewModelSelectionResolver.DefaultReviewModelId);
        }

        if (RequiresGemma12BMtpAssets(state.SelectedReviewModelId))
        {
            modelIds.Add(Gemma12BLocalValidation.MtpDraftModelId);
        }

        return modelIds
            .Where(static modelId => !string.IsNullOrWhiteSpace(modelId))
            .Select(modelId => installedModelRepository.FindInstalledModel(modelId!))
            .OfType<InstalledModel>()
            .ToArray();
    }

    private IReadOnlyList<ModelCatalogItem> GetSelectedCatalogModels(SetupState state)
    {
        var selected = new[]
        {
            (ModelId: state.SelectedAsrModelId, Role: "asr"),
            (ModelId: state.SelectedReviewModelId, Role: "review")
        };
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        return selected
            .Where(static item => !string.IsNullOrWhiteSpace(item.ModelId))
            .Select(item => catalog.Models.FirstOrDefault(model =>
                model.Role.Equals(item.Role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(item.ModelId, StringComparison.OrdinalIgnoreCase)))
            .OfType<ModelCatalogItem>()
            .ToArray();
    }

    private string GetSelectedReviewRuntimePath(string? modelId)
    {
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var effectiveModelId = !string.IsNullOrWhiteSpace(modelId)
            ? modelId
            : catalog.Presets?.FirstOrDefault(preset =>
                string.Equals(preset.PresetId, "recommended", StringComparison.OrdinalIgnoreCase))?.ReviewModelId;
        if (string.IsNullOrWhiteSpace(effectiveModelId))
        {
            return paths.LlamaCompletionPath;
        }

        return ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, effectiveModelId);
    }

    private static bool RequiresGemma12BMtpAssets(string? modelId)
    {
        return Gemma12BLocalValidation.IsTargetModel(modelId) &&
            Gemma12BLocalValidation.IsMtpServerEnabled();
    }

    private static bool RequiresDirectLlmStageFallback(string? modelId)
    {
        return Gemma12BLocalValidation.IsTargetModel(modelId);
    }

    private bool TryResolveReadyGemma12BMtpDraftPath(string? storageRoot, out string? path)
    {
        var configured = Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath();
        if (configured is not null)
        {
            path = configured;
            return LlamaRuntimePathBridge.CanPrepareModelPath(configured);
        }

        var installed = installedModelRepository.FindInstalledModel(Gemma12BLocalValidation.MtpDraftModelId);
        path = installed?.FilePath;
        var installedReady = installed is not null &&
            installed.Role.Equals("review_aux", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            File.Exists(installed.FilePath) &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath);
        if (installedReady)
        {
            return true;
        }

        path = string.IsNullOrWhiteSpace(storageRoot)
            ? Gemma12BLocalValidation.ResolveMtpDraftModelPath()
            : Gemma12BLocalValidation.ResolveMtpDraftModelPath(storageRoot);
        return LlamaRuntimePathBridge.CanPrepareModelPath(path);
    }
}
