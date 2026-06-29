using System.IO;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupModelSelectionService(
    AppPaths paths,
    SetupStateService stateService,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository,
    ISetupHostResourceProbe hostResourceProbe)
{
    public IReadOnlyList<ModelCatalogEntry> GetSelectableModels(string role)
    {
        return modelCatalogService.ListEntries()
            .Where(entry => entry.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                ModelCatalogCompatibility.IsSelectable(entry.CatalogItem))
            .ToArray();
    }

    public IReadOnlyList<ModelQualityPreset> GetModelPresets()
    {
        return modelCatalogService.LoadBuiltInCatalog().Presets ?? [];
    }

    public SetupState UseRecommendedSelections()
    {
        var recommendedPreset = GetModelPresets()
            .FirstOrDefault(preset => preset.PresetId.Equals("recommended", StringComparison.OrdinalIgnoreCase));
        if (recommendedPreset is not null)
        {
            return SelectPreset(recommendedPreset.PresetId);
        }

        var asrModel = PickRecommended("asr");
        var reviewModel = PickRecommended("review");
        var state = stateService.Load() with
        {
            IsCompleted = false,
            LastSmokeSucceeded = false,
            CurrentStep = SetupStep.AsrModel,
            SelectedModelPresetId = null,
            SelectedAsrModelId = asrModel?.ModelId,
            SelectedReviewModelId = reviewModel?.ModelId,
            StorageRoot = paths.DefaultModelStorageRoot
        };
        return stateService.Save(state);
    }

    public SetupState SelectPreset(string presetId, bool advanceToModelStep = true)
    {
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var preset = (catalog.Presets ?? [])
            .FirstOrDefault(candidate => candidate.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            throw new InvalidOperationException($"Model preset is not in the catalog: {presetId}");
        }

        EnsurePresetModelExists(catalog, preset.AsrModelId, "asr");
        EnsurePresetModelExists(catalog, preset.ReviewModelId, "review");

        var current = stateService.Load();
        var state = current with
        {
            IsCompleted = false,
            LastSmokeSucceeded = false,
            CurrentStep = advanceToModelStep ? SetupStep.AsrModel : current.CurrentStep,
            SetupMode = preset.PresetId,
            SelectedModelPresetId = preset.PresetId,
            SelectedAsrModelId = preset.AsrModelId,
            SelectedReviewModelId = preset.ReviewModelId,
            StorageRoot = string.IsNullOrWhiteSpace(current.StorageRoot)
                ? paths.DefaultModelStorageRoot
                : current.StorageRoot
        };
        return stateService.Save(state);
    }

    public SetupState SelectModel(string role, string modelId)
    {
        var catalogItem = ResolveSelectableCatalogItem(role, modelId);

        var state = stateService.Load();
        state = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state with { IsCompleted = false, LastSmokeSucceeded = false, CurrentStep = SetupStep.AsrModel, SetupMode = "custom", SelectedModelPresetId = null, SelectedAsrModelId = catalogItem.ModelId }
            : state with { IsCompleted = false, LastSmokeSucceeded = false, CurrentStep = SetupStep.ReviewModel, SetupMode = "custom", SelectedModelPresetId = null, SelectedReviewModelId = catalogItem.ModelId };
        return stateService.Save(state);
    }

    public SetupState SelectModelWithoutInvalidatingCompletion(string role, string modelId)
    {
        var catalogItem = ResolveSelectableCatalogItem(role, modelId);
        var state = stateService.Load();
        var canPreserveCompletion = state.IsCompleted && IsSelectionReadyForCompletion(catalogItem);
        state = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state with
            {
                IsCompleted = canPreserveCompletion,
                LastSmokeSucceeded = canPreserveCompletion && state.LastSmokeSucceeded,
                CurrentStep = canPreserveCompletion ? state.CurrentStep : SetupStep.AsrModel,
                SetupMode = "custom",
                SelectedModelPresetId = null,
                SelectedAsrModelId = catalogItem.ModelId
            }
            : state with
            {
                IsCompleted = canPreserveCompletion,
                LastSmokeSucceeded = canPreserveCompletion && state.LastSmokeSucceeded,
                CurrentStep = canPreserveCompletion ? state.CurrentStep : SetupStep.ReviewModel,
                SetupMode = "custom",
                SelectedModelPresetId = null,
                SelectedReviewModelId = catalogItem.ModelId
            };
        return stateService.Save(state);
    }

    public SetupState SetStorageRoot(string storageRoot)
    {
        var root = string.IsNullOrWhiteSpace(storageRoot) ? paths.DefaultModelStorageRoot : storageRoot.Trim();
        Directory.CreateDirectory(root);
        return stateService.Save(stateService.Load() with
        {
            IsCompleted = false,
            LastSmokeSucceeded = false,
            CurrentStep = SetupStep.Storage,
            StorageRoot = root
        });
    }

    public SetupState RepairUnsupportedSelections(SetupState state)
    {
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var preset = string.IsNullOrWhiteSpace(state.SelectedModelPresetId)
            ? null
            : (catalog.Presets ?? []).FirstOrDefault(candidate =>
                candidate.PresetId.Equals(state.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase));

        var repaired = RepairSelection(state, catalog, preset, "asr");
        repaired = RepairSelection(repaired, catalog, preset, "review");
        return repaired.Equals(state) ? state : stateService.Save(repaired);
    }

    public ModelCatalogItem? GetSelectedCatalogItem(string role)
    {
        var state = stateService.Load();
        var modelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state.SelectedAsrModelId
            : state.SelectedReviewModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return modelCatalogService.LoadBuiltInCatalog().Models
            .FirstOrDefault(model => model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    public ModelCatalogItem? GetCatalogItemById(string modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return modelCatalogService.LoadBuiltInCatalog().Models
            .FirstOrDefault(model => model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    private ModelCatalogEntry? PickRecommended(string role)
    {
        var recommendedModelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? "faster-whisper-large-v3-turbo"
            : ReviewModelSelectionResolver.DefaultReviewModelId;
        var selectableModels = GetSelectableModels(role);
        return selectableModels.FirstOrDefault(entry =>
            entry.ModelId.Equals(recommendedModelId, StringComparison.OrdinalIgnoreCase)) ??
            selectableModels.FirstOrDefault(entry =>
                entry.CatalogItem.RecommendedFor.Contains("fast_baseline", StringComparer.OrdinalIgnoreCase) ||
                entry.CatalogItem.RecommendedFor.Contains("review_default", StringComparer.OrdinalIgnoreCase)) ??
            selectableModels.FirstOrDefault();
    }

    private SetupState RepairSelection(
        SetupState state,
        ModelCatalog catalog,
        ModelQualityPreset? preset,
        string role)
    {
        var selectedModelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state.SelectedAsrModelId
            : state.SelectedReviewModelId;
        var selectedModel = FindCatalogModel(catalog, selectedModelId, role);
        if (selectedModel is not null && ModelCatalogCompatibility.IsSelectable(selectedModel))
        {
            return state;
        }

        if (!string.IsNullOrWhiteSpace(selectedModelId) && selectedModel is null)
        {
            return role.Equals("review", StringComparison.OrdinalIgnoreCase)
                ? state with
                {
                    IsCompleted = false,
                    LastSmokeSucceeded = false,
                    CurrentStep = SetupStep.ReviewModel,
                    SelectedReviewModelId = ReviewModelSelectionResolver.Resolve(
                        catalog,
                        selectedModelId,
                        selectedPresetId: preset?.PresetId)
                }
                : state;
        }

        var presetModelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? preset?.AsrModelId
            : preset?.ReviewModelId;
        var replacementModelId = IsSelectableCatalogModel(catalog, presetModelId, role)
            ? presetModelId
            : PickRecommended(role)?.ModelId;

        var repairedState = selectedModel is null
            ? state
            : state with
            {
                IsCompleted = false,
                LastSmokeSucceeded = false,
                CurrentStep = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
                    ? SetupStep.AsrModel
                    : SetupStep.ReviewModel
            };
        return role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? repairedState with { SelectedAsrModelId = replacementModelId }
            : repairedState with { SelectedReviewModelId = replacementModelId };
    }

    private static bool IsSelectableCatalogModel(ModelCatalog catalog, string? modelId, string role)
    {
        var model = FindCatalogModel(catalog, modelId, role);
        return model is not null && ModelCatalogCompatibility.IsSelectable(model);
    }

    private static ModelCatalogItem? FindCatalogModel(ModelCatalog catalog, string? modelId, string role)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : catalog.Models.FirstOrDefault(model =>
                model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    private ModelCatalogItem ResolveSelectableCatalogItem(string role, string modelId)
    {
        var catalogItem = modelCatalogService.LoadBuiltInCatalog().Models
            .FirstOrDefault(model => model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (catalogItem is null)
        {
            throw new InvalidOperationException($"Model is not in the catalog: {modelId}");
        }

        if (!ModelCatalogCompatibility.IsSelectable(catalogItem))
        {
            throw new InvalidOperationException($"Model is not selectable in setup: {modelId}");
        }

        return catalogItem;
    }

    private bool IsSelectionReadyForCompletion(ModelCatalogItem catalogItem)
    {
        if (catalogItem.Requirements.GpuRequired && !hostResourceProbe.GetResources().NvidiaGpuDetected)
        {
            return false;
        }

        var installed = GetReadyInstalledModel(catalogItem);
        if (installed is null)
        {
            return false;
        }

        return catalogItem.Role.Equals("review", StringComparison.OrdinalIgnoreCase)
            ? IsReviewRuntimeReady(catalogItem.ModelId) &&
              IsReviewRuntimePathBridgeReady(installed) &&
              IsDirectLlmFallbackReady(catalogItem.ModelId) &&
              IsGemma12BMtpDraftReady(catalogItem.ModelId)
            : FasterWhisperRuntimeLayout.HasPackage(paths);
    }

    private bool IsInstalledModelReady(ModelCatalogItem catalogItem)
    {
        return GetReadyInstalledModel(catalogItem) is not null;
    }

    private InstalledModel? GetReadyInstalledModel(ModelCatalogItem catalogItem)
    {
        var installed = installedModelRepository.FindInstalledModel(catalogItem.ModelId);
        return installed is not null &&
            installed.Role.Equals(catalogItem.Role, StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath))
            ? installed
            : null;
    }

    private bool IsReviewRuntimeReady(string modelId)
    {
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var runtimePath = ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, modelId);
        return File.Exists(runtimePath) &&
            (!RequiresGemma12BMtpAssets(modelId) ||
             Gemma12BLocalValidation.IsLlamaServerMtpCapable(
                 Gemma12BLocalValidation.ResolveLlamaServerPath(runtimePath),
                 LlamaRuntimeEnvironment.Build(paths)));
    }

    private bool IsDirectLlmFallbackReady(string modelId)
    {
        if (!Gemma12BLocalValidation.IsTargetModel(modelId) ||
            Gemma12BLocalValidation.IsMtpServerEnabled())
        {
            return true;
        }

        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var fallback = catalog.Models.FirstOrDefault(model =>
            model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            model.ModelId.Equals(ReviewModelSelectionResolver.DefaultReviewModelId, StringComparison.OrdinalIgnoreCase));
        return fallback is not null &&
            GetReadyInstalledModel(fallback) is not null;
    }

    private bool IsGemma12BMtpDraftReady(string modelId)
    {
        if (!RequiresGemma12BMtpAssets(modelId))
        {
            return true;
        }

        var configured = Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath();
        if (configured is not null)
        {
            return LlamaRuntimePathBridge.CanPrepareModelPath(configured);
        }

        var installed = installedModelRepository.FindInstalledModel(Gemma12BLocalValidation.MtpDraftModelId);
        if (installed is not null &&
            installed.Role.Equals("review_aux", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            File.Exists(installed.FilePath) &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath))
        {
            return true;
        }

        var storageRoot = stateService.Load().StorageRoot;
        var fallbackPath = string.IsNullOrWhiteSpace(storageRoot)
            ? Gemma12BLocalValidation.ResolveMtpDraftModelPath()
            : Gemma12BLocalValidation.ResolveMtpDraftModelPath(storageRoot);
        return LlamaRuntimePathBridge.CanPrepareModelPath(fallbackPath);
    }

    private static bool RequiresGemma12BMtpAssets(string modelId)
    {
        return Gemma12BLocalValidation.IsTargetModel(modelId) &&
            Gemma12BLocalValidation.IsMtpServerEnabled();
    }

    private static bool IsReviewRuntimePathBridgeReady(InstalledModel installed)
    {
        try
        {
            using var bridge = LlamaRuntimePathBridge.Create(installed.FilePath);
            return IsAscii(bridge.ModelPath);
        }
        catch (Exception exception) when (LlamaRuntimePathBridge.IsBridgePreparationException(exception)
            || exception is DirectoryNotFoundException or FileNotFoundException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool IsAscii(string value)
    {
        return value.All(static character => character <= 0x7f);
    }

    private static void EnsurePresetModelExists(ModelCatalog catalog, string modelId, string role)
    {
        if (!catalog.Models.Any(model => model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
            ModelCatalogCompatibility.IsSelectable(model)))
        {
            throw new InvalidOperationException($"Preset references missing {role} model: {modelId}");
        }
    }
}
