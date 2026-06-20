using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Tests;

public sealed class SetupWizardStatePresenterTests
{
    [Fact]
    public void Create_ProjectsDisplaySelectionsFromSnapshot()
    {
        var asrChoice = CreateModel("asr-large", "asr", "Large ASR");
        var reviewChoice = CreateModel("review-gemma", "review", "Gemma");
        var preset = new ModelQualityPreset(
            "balanced",
            "Balanced",
            "Balanced preset",
            "Balanced description",
            asrChoice.ModelId,
            reviewChoice.ModelId,
            [],
            []);
        var state = SetupState.Default("C:\\KoeNote");
        var displayState = state with
        {
            SelectedModelPresetId = preset.PresetId,
            SelectedAsrModelId = asrChoice.ModelId,
            SelectedReviewModelId = reviewChoice.ModelId
        };
        var snapshot = new SetupFlowSnapshot(
            new SetupPresetRecommendation(
                preset.PresetId,
                preset.DisplayName,
                "Recommended",
                new SetupHostResources(null, null, false, null, "Unknown")),
            state,
            displayState,
            displayState,
            [],
            [asrChoice],
            [reviewChoice],
            [preset],
            null,
            [],
            []);

        var presentation = SetupWizardStatePresenter.Create(snapshot);

        Assert.Same(asrChoice, presentation.SelectedAsrModel);
        Assert.Same(reviewChoice, presentation.SelectedReviewModel);
        Assert.Same(preset, presentation.SelectedModelPreset);
        Assert.Equal(snapshot.SelectionDraft, presentation.SelectionDraft);
    }

    [Fact]
    public void Create_InfersPresetFromMatchingModelSelectionWhenPresetIdIsMissing()
    {
        var asrChoice = CreateModel("asr-large", "asr", "Large ASR");
        var reviewChoice = CreateModel("review-gemma", "review", "Gemma");
        var preset = new ModelQualityPreset(
            "high_accuracy",
            "High accuracy",
            "High accuracy",
            "High accuracy preset",
            asrChoice.ModelId,
            reviewChoice.ModelId,
            [],
            []);
        var state = SetupState.Default("C:\\KoeNote");
        var displayState = state with
        {
            SetupMode = "custom",
            SelectedModelPresetId = null,
            SelectedAsrModelId = asrChoice.ModelId,
            SelectedReviewModelId = reviewChoice.ModelId
        };
        var snapshot = CreateSnapshot(state, displayState, [asrChoice], [reviewChoice], [preset]);

        var presentation = SetupWizardStatePresenter.Create(snapshot);

        Assert.Same(preset, presentation.SelectedModelPreset);
    }

    [Fact]
    public void Create_UsesCustomPresetDisplayWhenModelSelectionDoesNotMatchPreset()
    {
        var asrChoice = CreateModel("asr-custom", "asr", "Custom ASR");
        var reviewChoice = CreateModel("review-custom", "review", "Custom review");
        var preset = new ModelQualityPreset(
            "balanced",
            "Balanced",
            "Balanced",
            "Balanced preset",
            "asr-balanced",
            "review-balanced",
            [],
            []);
        var state = SetupState.Default("C:\\KoeNote");
        var displayState = state with
        {
            SetupMode = "custom",
            SelectedModelPresetId = null,
            SelectedAsrModelId = asrChoice.ModelId,
            SelectedReviewModelId = reviewChoice.ModelId
        };
        var snapshot = CreateSnapshot(state, displayState, [asrChoice], [reviewChoice], [preset]);

        var presentation = SetupWizardStatePresenter.Create(snapshot);

        Assert.NotNull(presentation.SelectedModelPreset);
        Assert.Equal(SetupWizardStatePresenter.CustomPresetId, presentation.SelectedModelPreset.PresetId);
        Assert.Equal("カスタム", presentation.SelectedModelPreset.DisplayName);
        Assert.Equal(asrChoice.ModelId, presentation.SelectedModelPreset.AsrModelId);
        Assert.Equal(reviewChoice.ModelId, presentation.SelectedModelPreset.ReviewModelId);
    }

    private static SetupFlowSnapshot CreateSnapshot(
        SetupState state,
        SetupState displayState,
        IReadOnlyList<ModelCatalogEntry> asrChoices,
        IReadOnlyList<ModelCatalogEntry> reviewChoices,
        IReadOnlyList<ModelQualityPreset> presets)
    {
        return new SetupFlowSnapshot(
            new SetupPresetRecommendation(
                presets.First().PresetId,
                presets.First().DisplayName,
                "Recommended",
                new SetupHostResources(null, null, false, null, "Unknown")),
            state,
            displayState,
            displayState,
            [],
            asrChoices,
            reviewChoices,
            presets,
            null,
            [],
            []);
    }

    private static ModelCatalogEntry CreateModel(string modelId, string role, string displayName)
    {
        return new ModelCatalogEntry(
            new ModelCatalogItem(
                modelId,
                "test",
                role,
                "test-engine",
                displayName,
                ["ja"],
                [],
                new ModelRuntimeSpec("file", modelId),
                new ModelDownloadSpec("manual", null, null),
                new ModelLicenseSpec("Test", null),
                new ModelRequirements(false, 0, false),
                "stable"),
            InstalledModel: null);
    }
}
