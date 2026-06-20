using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal static class SetupWizardStatePresenter
{
    public const string CustomPresetId = "__custom__";

    public static SetupWizardPresentationState Create(SetupFlowSnapshot snapshot)
    {
        var displayState = snapshot.DisplayState;
        return new SetupWizardPresentationState(
            snapshot.Recommendation,
            snapshot.State,
            snapshot.SelectionDraft,
            FindModel(snapshot.AsrModelChoices, displayState.SelectedAsrModelId) ??
                snapshot.AsrModelChoices.FirstOrDefault(),
            FindModel(snapshot.ReviewModelChoices, displayState.SelectedReviewModelId) ??
                snapshot.ReviewModelChoices.FirstOrDefault(),
            FindModel(snapshot.ReviewModelChoices, snapshot.State.SelectedReviewModelId) ??
                snapshot.ReviewModelChoices.FirstOrDefault(),
            ResolvePreset(snapshot.PresetChoices, displayState));
    }

    private static ModelCatalogEntry? FindModel(IEnumerable<ModelCatalogEntry> choices, string? modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : choices.FirstOrDefault(entry => entry.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
    }

    private static ModelQualityPreset? ResolvePreset(
        IEnumerable<ModelQualityPreset> presets,
        SetupState displayState)
    {
        var presetChoices = presets.ToArray();
        var selectedPreset = string.IsNullOrWhiteSpace(displayState.SelectedModelPresetId)
            ? null
            : presetChoices.FirstOrDefault(preset =>
                preset.PresetId.Equals(displayState.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase));
        if (selectedPreset is not null)
        {
            return selectedPreset;
        }

        var matchingPreset = presetChoices.FirstOrDefault(preset =>
            preset.AsrModelId.Equals(displayState.SelectedAsrModelId, StringComparison.OrdinalIgnoreCase) &&
            preset.ReviewModelId.Equals(displayState.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase));
        if (matchingPreset is not null)
        {
            return matchingPreset;
        }

        return string.IsNullOrWhiteSpace(displayState.SelectedAsrModelId) ||
            string.IsNullOrWhiteSpace(displayState.SelectedReviewModelId)
            ? null
            : new ModelQualityPreset(
                CustomPresetId,
                "カスタム",
                "カスタム",
                "個別に選択したモデル構成です。",
                displayState.SelectedAsrModelId,
                displayState.SelectedReviewModelId,
                [],
                []);
    }
}

internal sealed record SetupWizardPresentationState(
    SetupPresetRecommendation Recommendation,
    SetupState State,
    SetupState? SelectionDraft,
    ModelCatalogEntry? SelectedAsrModel,
    ModelCatalogEntry? SelectedReviewModel,
    ModelCatalogEntry? SelectedSettingsReviewModel,
    ModelQualityPreset? SelectedModelPreset);
