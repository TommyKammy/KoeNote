using System.IO;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal sealed class SetupModelSelectionService(
    AppPaths paths,
    SetupStateService stateService,
    ModelCatalogService modelCatalogService)
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
        var catalogItem = modelCatalogService.LoadBuiltInCatalog().Models
            .FirstOrDefault(model => model.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
        if (catalogItem is null)
        {
            throw new InvalidOperationException($"Model is not in the catalog: {modelId}");
        }

        var state = stateService.Load();
        state = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state with { CurrentStep = SetupStep.AsrModel, SetupMode = "custom", SelectedModelPresetId = null, SelectedAsrModelId = catalogItem.ModelId }
            : state with { CurrentStep = SetupStep.ReviewModel, SetupMode = "custom", SelectedModelPresetId = null, SelectedReviewModelId = catalogItem.ModelId };
        return stateService.Save(state);
    }

    public SetupState SetStorageRoot(string storageRoot)
    {
        var root = string.IsNullOrWhiteSpace(storageRoot) ? paths.DefaultModelStorageRoot : storageRoot.Trim();
        Directory.CreateDirectory(root);
        return stateService.Save(stateService.Load() with
        {
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

    private ModelCatalogEntry? PickRecommended(string role)
    {
        var recommendedModelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? "faster-whisper-large-v3-turbo"
            : "gemma-4-e4b-it-q4-k-m";
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
            return state;
        }

        var presetModelId = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? preset?.AsrModelId
            : preset?.ReviewModelId;
        var replacementModelId = IsSelectableCatalogModel(catalog, presetModelId, role)
            ? presetModelId
            : PickRecommended(role)?.ModelId;

        return role.Equals("asr", StringComparison.OrdinalIgnoreCase)
            ? state with { SelectedAsrModelId = replacementModelId }
            : state with { SelectedReviewModelId = replacementModelId };
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
