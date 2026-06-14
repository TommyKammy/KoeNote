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
        var catalog = modelCatalogService.LoadBuiltInCatalog();
        var modelId = ResolveReviewModelId(catalog, setupStateService.Load());
        var profile = new LlmProfileResolver(paths, installedModelRepository).Resolve(catalog, modelId);
        var activeProfile = settingsRepository.FindActiveProfile();

        if (!overwriteActive && activeProfile is not null)
        {
            if (CanRefreshSetupManagedProfile(activeProfile))
            {
                UpsertSetupProfileAndTasks(profile);
                return true;
            }

            return false;
        }

        UpsertSetupProfileAndTasks(profile);
        return true;
    }

    private void UpsertSetupProfileAndTasks(LlmRuntimeProfile profile)
    {
        settingsRepository.UpsertProfile(profile, isActive: true, source: "setup-state");

        var taskResolver = new LlmTaskSettingsResolver();
        foreach (var taskKind in new[] { LlmTaskKind.Review, LlmTaskKind.Summary, LlmTaskKind.Polishing })
        {
            settingsRepository.UpsertTaskSettings(profile.ProfileId, taskResolver.Resolve(profile, taskKind));
        }
    }

    private static bool CanRefreshSetupManagedProfile(PersistedLlmRuntimeProfile activeProfile)
    {
        return activeProfile.Source.Equals("setup-state", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveReviewModelId(ModelCatalog catalog, SetupState state)
    {
        return ReviewModelSelectionResolver.Resolve(catalog, state.SelectedReviewModelId, state.SelectedModelPresetId);
    }
}
