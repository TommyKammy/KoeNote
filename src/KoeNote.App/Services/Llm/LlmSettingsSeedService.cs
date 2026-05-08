using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Services.Llm;

public sealed class LlmSettingsSeedService(
    AppPaths paths,
    ModelCatalogService modelCatalogService,
    InstalledModelRepository installedModelRepository,
    SetupStateService setupStateService,
    LlmSettingsRepository settingsRepository)
{
    public bool EnsureActiveProfileFromSetup(bool overwriteActive = false)
    {
        if (!overwriteActive && settingsRepository.FindActiveProfile() is not null)
        {
            return false;
        }

        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var modelId = ResolveReviewModelId(catalog, setupStateService.Load());
        var profile = new LlmProfileResolver(paths, installedModelRepository).Resolve(catalog, modelId);
        settingsRepository.UpsertProfile(profile, isActive: true, source: "setup-state");

        var taskResolver = new LlmTaskSettingsResolver();
        foreach (var taskKind in new[] { LlmTaskKind.Review, LlmTaskKind.Summary, LlmTaskKind.Polishing })
        {
            settingsRepository.UpsertTaskSettings(profile.ProfileId, taskResolver.Resolve(profile, taskKind));
        }

        return true;
    }

    private static string ResolveReviewModelId(ModelCatalog catalog, SetupState state)
    {
        if (IsSelectableReviewModel(catalog, state.SelectedReviewModelId))
        {
            return state.SelectedReviewModelId!;
        }

        var presetReviewModelId = (catalog.Presets ?? [])
            .FirstOrDefault(preset => !string.IsNullOrWhiteSpace(state.SelectedModelPresetId) &&
                preset.PresetId.Equals(state.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase))
            ?.ReviewModelId;
        return IsSelectableReviewModel(catalog, presetReviewModelId)
            ? presetReviewModelId!
            : LlmProfileResolver.FallbackReviewModelId;
    }

    private static bool IsSelectableReviewModel(ModelCatalog catalog, string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            catalog.Models.Any(model =>
                model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
                ModelCatalogCompatibility.IsSelectable(model));
    }
}
