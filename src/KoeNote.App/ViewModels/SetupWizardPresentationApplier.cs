using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.ViewModels;

internal static class SetupWizardPresentationApplier
{
    public static AppliedSetupWizardPresentation Apply(SetupWizardPresentationState presentation)
    {
        return new AppliedSetupWizardPresentation(
            presentation.Recommendation,
            presentation.State,
            presentation.SelectionDraft,
            presentation.SelectedAsrModel,
            presentation.SelectedReviewModel,
            presentation.SelectedSettingsReviewModel,
            presentation.SelectedModelPreset);
    }
}

internal sealed record AppliedSetupWizardPresentation(
    SetupPresetRecommendation Recommendation,
    SetupState State,
    SetupState? SelectionDraft,
    ModelCatalogEntry? SelectedAsrModel,
    ModelCatalogEntry? SelectedReviewModel,
    ModelCatalogEntry? SelectedSettingsReviewModel,
    ModelQualityPreset? SelectedModelPreset);
