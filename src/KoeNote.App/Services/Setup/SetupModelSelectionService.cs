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
            .Where(entry => entry.Role.Equals(role, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public SetupState UseRecommendedSelections()
    {
        var asrModel = PickRecommended("asr");
        var reviewModel = PickRecommended("review");
        var state = stateService.Load() with
        {
            CurrentStep = SetupStep.AsrModel,
            SelectedAsrModelId = asrModel?.ModelId,
            SelectedReviewModelId = reviewModel?.ModelId,
            StorageRoot = paths.DefaultModelStorageRoot
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
            ? state with { CurrentStep = SetupStep.AsrModel, SelectedAsrModelId = catalogItem.ModelId }
            : state with { CurrentStep = SetupStep.ReviewModel, SelectedReviewModelId = catalogItem.ModelId };
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
        return GetSelectableModels(role)
            .FirstOrDefault(entry => entry.CatalogItem.RecommendedFor.Contains("v0.1", StringComparer.OrdinalIgnoreCase)) ??
            GetSelectableModels(role).FirstOrDefault();
    }
}
