using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal static class SetupWizardStatePresenter
{
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
            snapshot.PresetChoices.FirstOrDefault(preset =>
                preset.PresetId.Equals(displayState.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase)));
    }

    private static ModelCatalogEntry? FindModel(IEnumerable<ModelCatalogEntry> choices, string? modelId)
    {
        return string.IsNullOrWhiteSpace(modelId)
            ? null
            : choices.FirstOrDefault(entry => entry.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
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
