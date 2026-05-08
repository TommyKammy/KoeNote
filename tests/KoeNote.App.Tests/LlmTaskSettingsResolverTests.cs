using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlmTaskSettingsResolverTests
{
    [Fact]
    public void Resolve_ReturnsCurrentReviewSettingsForStandardModel()
    {
        var settings = Resolve("gemma-4-e4b-it-q4-k-m", LlmTaskKind.Review);

        Assert.Equal(LlmTaskKind.Review, settings.TaskKind);
        Assert.Equal("default", settings.PromptTemplateId);
        Assert.Equal("current", settings.PromptVersion);
        Assert.Equal("gemma-review-balanced", settings.GenerationProfile);
        Assert.Equal(0.1, settings.Temperature);
        Assert.Equal(4096, settings.MaxTokens);
        Assert.Equal(80, settings.ChunkSegmentCount);
        Assert.True(settings.UseJsonSchema);
        Assert.True(settings.EnableRepair);
        Assert.Equal("json_schema", settings.ValidationMode);
    }

    [Fact]
    public void Resolve_ReturnsCurrentSummarySettingsForStandardModel()
    {
        var settings = Resolve("gemma-4-e4b-it-q4-k-m", LlmTaskKind.Summary);

        Assert.Equal(LlmTaskKind.Summary, settings.TaskKind);
        Assert.Equal("gemma-structured", settings.PromptTemplateId);
        Assert.Equal(TranscriptSummaryPromptBuilder.PromptVersion, settings.PromptVersion);
        Assert.Equal("gemma-summary-balanced", settings.GenerationProfile);
        Assert.Equal(0.1, settings.Temperature);
        Assert.Equal(1024, settings.MaxTokens);
        Assert.Equal(80, settings.ChunkSegmentCount);
        Assert.False(settings.UseJsonSchema);
        Assert.False(settings.EnableRepair);
        Assert.Equal("markdown_summary_sections", settings.ValidationMode);
    }

    [Fact]
    public void Resolve_ReturnsCurrentPolishingSettingsForStandardModel()
    {
        var settings = Resolve("gemma-4-e4b-it-q4-k-m", LlmTaskKind.Polishing);

        Assert.Equal(LlmTaskKind.Polishing, settings.TaskKind);
        Assert.Equal(TranscriptPolishingPromptBuilder.PromptVersion, settings.PromptVersion);
        Assert.Equal("gemma-polishing-balanced", settings.GenerationProfile);
        Assert.Equal(4096, settings.MaxTokens);
        Assert.Equal(80, settings.ChunkSegmentCount);
        Assert.False(settings.UseJsonSchema);
        Assert.False(settings.EnableRepair);
        Assert.Equal("markdown_non_empty", settings.ValidationMode);
    }

    [Fact]
    public void Resolve_PreservesCurrentTernaryReviewAndSummaryLimits()
    {
        var review = Resolve("ternary-bonsai-8b-q2-0", LlmTaskKind.Review);
        var summary = Resolve("ternary-bonsai-8b-q2-0", LlmTaskKind.Summary);

        Assert.Equal("compact", review.PromptTemplateId);
        Assert.Equal(192, review.MaxTokens);
        Assert.Equal(3, review.ChunkSegmentCount);
        Assert.False(review.UseJsonSchema);
        Assert.False(review.EnableRepair);
        Assert.Equal("bonsai-compact", summary.PromptTemplateId);
        Assert.Equal("ternary-bonsai-summary", summary.GenerationProfile);
        Assert.Equal(512, summary.MaxTokens);
        Assert.Equal(3, summary.ChunkSegmentCount);
    }

    [Fact]
    public void Resolve_UsesConservativeSummaryAndPolishingSettingsForBonsaiQ1()
    {
        var summary = Resolve("bonsai-8b-q1-0", LlmTaskKind.Summary);
        var polishing = Resolve("bonsai-8b-q1-0", LlmTaskKind.Polishing);

        Assert.Equal("bonsai-summary-conservative", summary.GenerationProfile);
        Assert.Equal("bonsai-compact", summary.PromptTemplateId);
        Assert.Equal(512, summary.MaxTokens);
        Assert.Equal(40, summary.ChunkSegmentCount);
        Assert.Equal("bonsai-polishing-conservative", polishing.GenerationProfile);
        Assert.Equal(2048, polishing.MaxTokens);
        Assert.Equal(40, polishing.ChunkSegmentCount);
    }

    [Fact]
    public void Resolve_UsesFallbackPresetForUnknownModels()
    {
        var review = Resolve("custom-model-q4", LlmTaskKind.Review);
        var summary = Resolve("custom-model-q4", LlmTaskKind.Summary);

        Assert.Equal("fallback-review-balanced", review.GenerationProfile);
        Assert.Equal(4096, review.MaxTokens);
        Assert.Equal("fallback-summary-balanced", summary.GenerationProfile);
        Assert.Equal(768, summary.MaxTokens);
    }

    private static LlmTaskSettings Resolve(string modelId, LlmTaskKind taskKind)
    {
        var profile = new LlmRuntimeProfile(
            $"builtin:{modelId}",
            modelId,
            ResolveFamily(modelId),
            modelId,
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
        return new LlmTaskSettingsResolver().Resolve(profile, taskKind);
    }

    private static string? ResolveFamily(string modelId) =>
        modelId.Contains("gemma", StringComparison.OrdinalIgnoreCase) ? "gemma" :
        modelId.Contains("bonsai", StringComparison.OrdinalIgnoreCase) ? "bonsai" :
        modelId.Contains("llm-jp", StringComparison.OrdinalIgnoreCase) ? "llm-jp" :
        null;
}
