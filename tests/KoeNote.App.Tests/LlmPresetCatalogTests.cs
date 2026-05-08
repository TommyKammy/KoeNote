using KoeNote.App.Services.Llm;

namespace KoeNote.App.Tests;

public sealed class LlmPresetCatalogTests
{
    [Theory]
    [InlineData("gemma-4-e4b-it-q4-k-m", "gemma", "gemma:balanced", 8192, 999)]
    [InlineData("bonsai-8b-q1-0", "bonsai", "bonsai:conservative", 8192, 999)]
    [InlineData("llm-jp-4-8b-thinking-q4-k-m", "llm-jp", "llm-jp:balanced", 8192, 999)]
    [InlineData("unknown-q4", null, "fallback:balanced", 8192, 999)]
    public void ResolveRuntimePreset_ReturnsExpectedPreset(
        string modelId,
        string? family,
        string presetId,
        int contextSize,
        int gpuLayers)
    {
        var preset = LlmPresetCatalog.ResolveRuntimePreset(modelId, family);

        Assert.Equal(presetId, preset.PresetId);
        Assert.Equal(contextSize, preset.ContextSize);
        Assert.Equal(gpuLayers, preset.GpuLayers);
        Assert.True(preset.NoConversation);
    }

    [Fact]
    public void ResolveRuntimePreset_ReturnsTernaryBonsaiCpuBoundedPreset()
    {
        var preset = LlmPresetCatalog.ResolveRuntimePreset("ternary-bonsai-8b-q2-0", "bonsai");

        Assert.Equal("ternary-bonsai:cpu-bounded", preset.PresetId);
        Assert.Equal(TimeSpan.FromMinutes(20), preset.Timeout);
        Assert.Equal(1024, preset.ContextSize);
        Assert.Equal(0, preset.GpuLayers);
        Assert.NotNull(preset.Threads);
        Assert.NotNull(preset.ThreadsBatch);
    }

    [Theory]
    [InlineData("gemma-4-e4b-it-q4-k-m", "gemma", "gemma-structured", "markdown_summary_sections")]
    [InlineData("bonsai-8b-q1-0", "bonsai", "bonsai-compact", "markdown_summary_sections")]
    [InlineData("llm-jp-4-8b-thinking-q4-k-m", "llm-jp", "llm-jp-structured", "markdown_summary_sections")]
    public void ResolveTaskSettings_ReturnsModelSpecificSummaryPromptAndValidation(
        string modelId,
        string family,
        string promptTemplateId,
        string validationMode)
    {
        var settings = LlmPresetCatalog.ResolveTaskSettings(modelId, family, LlmTaskKind.Summary);

        Assert.Equal(promptTemplateId, settings.PromptTemplateId);
        Assert.Equal(validationMode, settings.ValidationMode);
    }
}
