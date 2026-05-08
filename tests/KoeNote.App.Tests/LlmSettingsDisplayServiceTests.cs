using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlmSettingsDisplayServiceTests
{
    [Fact]
    public void LoadSnapshot_ReturnsNotConfiguredWhenNoActiveProfileExists()
    {
        var service = new LlmSettingsDisplayService(new LlmSettingsRepository(TestDatabase.CreateReadyPaths()));

        var snapshot = service.LoadSnapshot();

        Assert.Equal("Not configured", snapshot.ActiveProfileSummary);
        Assert.Equal("Not configured", snapshot.HeaderReviewSummary);
        Assert.Equal("Not configured", snapshot.HeaderSummarySummary);
    }

    [Fact]
    public void LoadSnapshot_FormatsActiveProfileAndTaskSettings()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new LlmSettingsRepository(paths);
        var profile = new LlmRuntimeProfile(
            "builtin:gemma:balanced",
            "gemma-4-e4b-it-q4-k-m",
            "gemma",
            "Gemma 4 E4B it Q4_K_M",
            "llama-cpp",
            "runtime-llama-cpp",
            "model.gguf",
            "llama-completion.exe",
            8192,
            999,
            null,
            null,
            true,
            LlmOutputSanitizerProfiles.None,
            TimeSpan.FromHours(2));
        repository.UpsertProfile(profile, isActive: true, source: "test");
        repository.UpsertTaskSettings(profile.ProfileId, LlmPresetCatalog.ResolveTaskSettings(profile.ModelId, profile.ModelFamily, LlmTaskKind.Review));
        repository.UpsertTaskSettings(profile.ProfileId, LlmPresetCatalog.ResolveTaskSettings(profile.ModelId, profile.ModelFamily, LlmTaskKind.Summary));

        var snapshot = new LlmSettingsDisplayService(repository).LoadSnapshot();

        Assert.Contains("Gemma 4 E4B it Q4_K_M", snapshot.ActiveProfileSummary);
        Assert.Contains("ctx 8192", snapshot.ActiveProfileSummary);
        Assert.Contains("Review: gemma-review-balanced", snapshot.ReviewTaskSummary);
        Assert.Contains("Summary: gemma-summary-balanced", snapshot.SummaryTaskSummary);
        Assert.Equal("gemma-review-balanced, 4096 tok", snapshot.HeaderReviewSummary);
        Assert.Equal("gemma-summary-balanced, 1024 tok", snapshot.HeaderSummarySummary);
        Assert.Equal("Polishing: Not configured", snapshot.PolishingTaskSummary);
    }
}
