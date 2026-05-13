using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class ReadablePolishingPromptSettingsRepositoryTests
{
    [Theory]
    [InlineData(ReadablePolishingPromptModelFamilies.Gemma, TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId)]
    [InlineData(ReadablePolishingPromptModelFamilies.Bonsai, TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId)]
    [InlineData(ReadablePolishingPromptModelFamilies.LlmJp, TranscriptPolishingPromptBuilder.LlmJpPromptTemplateId)]
    public void Load_ReturnsModelSpecificDefaultWhenNotSaved(string modelFamily, string promptTemplateId)
    {
        var repository = new ReadablePolishingPromptSettingsRepository(TestDatabase.CreateReadyPaths());

        var persisted = repository.Load(modelFamily);

        Assert.Equal(modelFamily, persisted.Settings.ModelFamily);
        Assert.Equal(ReadablePolishingPromptPresets.StrongPunctuation, persisted.Settings.PresetId);
        Assert.Equal(promptTemplateId, persisted.Settings.PromptTemplateId);
        Assert.Equal(TranscriptPolishingPromptBuilder.PromptVersion, persisted.Settings.PromptVersion);
        Assert.False(persisted.Settings.UseCustomPrompt);
        Assert.Empty(persisted.Settings.AdditionalInstruction);
        Assert.Empty(persisted.Settings.CustomPrompt);
    }

    [Fact]
    public void Save_PersistsAndReadsPromptSettings()
    {
        var repository = new ReadablePolishingPromptSettingsRepository(TestDatabase.CreateReadyPaths());
        var settings = new ReadablePolishingPromptSettings(
            ReadablePolishingPromptModelFamilies.Gemma,
            ReadablePolishingPromptPresets.StrongPunctuation,
            "  短い行の連続は自然な文に結合してください。  ",
            UseCustomPrompt: false,
            string.Empty,
            TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId,
            "custom-v1");

        repository.Save(settings);

        var persisted = repository.Load(ReadablePolishingPromptModelFamilies.Gemma);
        Assert.Equal(ReadablePolishingPromptPresets.StrongPunctuation, persisted.Settings.PresetId);
        Assert.Equal("短い行の連続は自然な文に結合してください。", persisted.Settings.AdditionalInstruction);
        Assert.False(persisted.Settings.UseCustomPrompt);
        Assert.Equal("custom-v1", persisted.Settings.PromptVersion);
        Assert.True(persisted.UpdatedAt >= persisted.CreatedAt);
    }

    [Fact]
    public void Save_NormalizesUnsupportedValuesToSafeDefaults()
    {
        var repository = new ReadablePolishingPromptSettingsRepository(TestDatabase.CreateReadyPaths());
        var settings = new ReadablePolishingPromptSettings(
            "unknown-family",
            "unsupported-preset",
            "  extra instruction  ",
            UseCustomPrompt: true,
            "   ",
            string.Empty,
            string.Empty);

        repository.Save(settings);

        var persisted = repository.Load(ReadablePolishingPromptModelFamilies.Gemma);
        Assert.Equal(ReadablePolishingPromptModelFamilies.Gemma, persisted.Settings.ModelFamily);
        Assert.Equal(ReadablePolishingPromptPresets.Standard, persisted.Settings.PresetId);
        Assert.Equal("extra instruction", persisted.Settings.AdditionalInstruction);
        Assert.False(persisted.Settings.UseCustomPrompt);
        Assert.Empty(persisted.Settings.CustomPrompt);
        Assert.Equal(TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId, persisted.Settings.PromptTemplateId);
        Assert.Equal(TranscriptPolishingPromptBuilder.PromptVersion, persisted.Settings.PromptVersion);
    }

    [Fact]
    public void Save_UsesCustomPresetOnlyWhenCustomPromptIsPresent()
    {
        var repository = new ReadablePolishingPromptSettingsRepository(TestDatabase.CreateReadyPaths());
        repository.Save(new ReadablePolishingPromptSettings(
            ReadablePolishingPromptModelFamilies.Bonsai,
            ReadablePolishingPromptPresets.Standard,
            string.Empty,
            UseCustomPrompt: true,
            "  Rewrite carefully.  ",
            TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId,
            TranscriptPolishingPromptBuilder.PromptVersion));

        var persisted = repository.Load(ReadablePolishingPromptModelFamilies.Bonsai);
        Assert.Equal(ReadablePolishingPromptPresets.Custom, persisted.Settings.PresetId);
        Assert.True(persisted.Settings.UseCustomPrompt);
        Assert.Equal("Rewrite carefully.", persisted.Settings.CustomPrompt);
    }

    [Fact]
    public void LoadAll_ReturnsDefaultsForUnsavedModelFamilies()
    {
        var repository = new ReadablePolishingPromptSettingsRepository(TestDatabase.CreateReadyPaths());
        repository.Save(ReadablePolishingPromptSettings.CreateDefault(ReadablePolishingPromptModelFamilies.LlmJp) with
        {
            PresetId = ReadablePolishingPromptPresets.LectureSeminar
        });

        var settings = repository.LoadAll();

        Assert.Equal(ReadablePolishingPromptModelFamilies.Supported.Order(StringComparer.Ordinal), settings.Select(static item => item.Settings.ModelFamily));
        Assert.Contains(settings, item =>
            item.Settings.ModelFamily == ReadablePolishingPromptModelFamilies.LlmJp &&
            item.Settings.PresetId == ReadablePolishingPromptPresets.LectureSeminar);
        Assert.Contains(settings, item =>
            item.Settings.ModelFamily == ReadablePolishingPromptModelFamilies.Gemma &&
            item.Settings.PresetId == ReadablePolishingPromptPresets.StrongPunctuation);
    }

    [Fact]
    public void Reset_RemovesSavedSettingsAndRestoresDefaultOnLoad()
    {
        var repository = new ReadablePolishingPromptSettingsRepository(TestDatabase.CreateReadyPaths());
        repository.Save(ReadablePolishingPromptSettings.CreateDefault(ReadablePolishingPromptModelFamilies.Gemma) with
        {
            PresetId = ReadablePolishingPromptPresets.MeetingMinutes
        });

        repository.Reset(ReadablePolishingPromptModelFamilies.Gemma);

        Assert.Equal(ReadablePolishingPromptPresets.StrongPunctuation, repository.Load(ReadablePolishingPromptModelFamilies.Gemma).Settings.PresetId);
    }

    [Theory]
    [InlineData("gemma-4-e4b-it-q4-k-m", "review", ReadablePolishingPromptModelFamilies.Gemma)]
    [InlineData("bonsai-8b-q4-k-m", null, ReadablePolishingPromptModelFamilies.Bonsai)]
    [InlineData("model", "llm-jp", ReadablePolishingPromptModelFamilies.LlmJp)]
    [InlineData("unknown", "unknown", ReadablePolishingPromptModelFamilies.Gemma)]
    public void ResolveForModel_MapsModelAndCatalogFamilyToPromptFamily(string modelId, string? modelFamily, string expected)
    {
        Assert.Equal(expected, ReadablePolishingPromptModelFamilies.ResolveForModel(modelId, modelFamily));
    }
}
