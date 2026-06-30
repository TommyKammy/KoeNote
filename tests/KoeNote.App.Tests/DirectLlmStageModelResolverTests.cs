using KoeNote.App.Services;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class DirectLlmStageModelResolverTests
{
    [Fact]
    public void Resolve_FallsBackToDefaultReviewModelWhenGemma12BIsSelected()
    {
        var catalog = LoadCatalog();

        var modelId = DirectLlmStageModelResolver.Resolve(
            catalog,
            Gemma12BLocalValidation.ModelId,
            selectedPresetId: null);

        Assert.Equal(ReviewModelSelectionResolver.DefaultReviewModelId, modelId);
    }

    [Fact]
    public void Resolve_FallsBackToDefaultReviewModelWhenHighAccuracyPresetSelectsGemma12B()
    {
        var catalog = LoadCatalog();

        var modelId = DirectLlmStageModelResolver.Resolve(
            catalog,
            selectedModelId: null,
            selectedPresetId: "high_accuracy");

        Assert.Equal(ReviewModelSelectionResolver.DefaultReviewModelId, modelId);
    }

    [Fact]
    public void Resolve_AllowsGemma12BWhenMtpServerIsAvailable()
    {
        var catalog = LoadCatalog();

        var modelId = DirectLlmStageModelResolver.Resolve(
            catalog,
            selectedModelId: null,
            selectedPresetId: "high_accuracy",
            allowGemma12BMtpServer: true);

        Assert.Equal(Gemma12BLocalValidation.ModelId, modelId);
    }

    [Fact]
    public void Resolve_KeepsNonGemma12BSelection()
    {
        var catalog = LoadCatalog();

        var modelId = DirectLlmStageModelResolver.Resolve(
            catalog,
            "bonsai-8b-q1-0",
            selectedPresetId: null);

        Assert.Equal("bonsai-8b-q1-0", modelId);
    }

    private static ModelCatalog LoadCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return new ModelCatalogService(paths).LoadBuiltInCatalog();
    }
}
