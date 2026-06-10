using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

public sealed class SetupFlowCoordinator(AppPaths paths, SetupWizardService setupWizardService)
{
    public SetupFlowSnapshot BuildSnapshot(SetupState? selectionDraft, bool refreshSmokeChecks)
    {
        var recommendation = setupWizardService.GetPresetRecommendation();
        var state = setupWizardService.LoadState();
        var asrModelChoices = setupWizardService.GetSelectableModels("asr");
        var reviewModelChoices = setupWizardService.GetSelectableModels("review");
        var presetChoices = setupWizardService.GetModelPresets();
        var displayState = selectionDraft ?? CreateAutomaticRecommendationDraft(
            state,
            recommendation,
            presetChoices,
            asrModelChoices,
            reviewModelChoices);
        var nextSelectionDraft = selectionDraft ?? (displayState.Equals(state) ? null : displayState);

        return new SetupFlowSnapshot(
            recommendation,
            state,
            displayState,
            nextSelectionDraft,
            setupWizardService.BuildStepItems(state),
            asrModelChoices,
            reviewModelChoices,
            presetChoices,
            refreshSmokeChecks
                ? setupWizardService.GetEnvironmentChecks()
                    .Select(static check => new SetupSmokeCheck(check.Name, check.IsOk, check.Detail))
                    .ToArray()
                : null,
            setupWizardService.GetSelectedModelAudit(),
            setupWizardService.GetExistingDataSummary());
    }

    public SetupState CreatePresetDraft(
        SetupState state,
        string presetId,
        IReadOnlyCollection<ModelQualityPreset> presetChoices,
        IReadOnlyCollection<ModelCatalogEntry> asrModelChoices,
        IReadOnlyCollection<ModelCatalogEntry> reviewModelChoices)
    {
        var preset = presetChoices.FirstOrDefault(candidate =>
            candidate.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            throw new InvalidOperationException($"Model preset is not in the catalog: {presetId}");
        }

        if (!asrModelChoices.Any(entry => entry.ModelId.Equals(preset.AsrModelId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Preset references missing asr model: {preset.AsrModelId}");
        }

        if (!reviewModelChoices.Any(entry => entry.ModelId.Equals(preset.ReviewModelId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Preset references missing review model: {preset.ReviewModelId}");
        }

        return state with
        {
            IsCompleted = false,
            LastSmokeSucceeded = false,
            SetupMode = preset.PresetId,
            SelectedModelPresetId = preset.PresetId,
            SelectedAsrModelId = preset.AsrModelId,
            SelectedReviewModelId = preset.ReviewModelId,
            StorageRoot = string.IsNullOrWhiteSpace(state.StorageRoot)
                ? paths.DefaultModelStorageRoot
                : state.StorageRoot
        };
    }

    private SetupState CreateAutomaticRecommendationDraft(
        SetupState state,
        SetupPresetRecommendation recommendation,
        IReadOnlyCollection<ModelQualityPreset> presetChoices,
        IReadOnlyCollection<ModelCatalogEntry> asrModelChoices,
        IReadOnlyCollection<ModelCatalogEntry> reviewModelChoices)
    {
        if (state.IsCompleted ||
            state.CurrentStep is not (SetupStep.Welcome or SetupStep.SetupMode) ||
            !string.Equals(state.SelectedModelPresetId, "recommended", StringComparison.OrdinalIgnoreCase))
        {
            return state;
        }

        var presetId = recommendation.PresetId;
        if (string.IsNullOrWhiteSpace(presetId) ||
            presetId.Equals(state.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase) ||
            !presetChoices.Any(preset => preset.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        return CreatePresetDraft(state, presetId, presetChoices, asrModelChoices, reviewModelChoices);
    }
}

public sealed record SetupFlowSnapshot(
    SetupPresetRecommendation Recommendation,
    SetupState State,
    SetupState DisplayState,
    SetupState? SelectionDraft,
    IReadOnlyList<SetupStepItem> StepItems,
    IReadOnlyList<ModelCatalogEntry> AsrModelChoices,
    IReadOnlyList<ModelCatalogEntry> ReviewModelChoices,
    IReadOnlyList<ModelQualityPreset> PresetChoices,
    IReadOnlyList<SetupSmokeCheck>? SmokeChecks,
    IReadOnlyList<SetupModelAudit> ModelAudits,
    IReadOnlyList<SetupExistingDataItem> ExistingData);
