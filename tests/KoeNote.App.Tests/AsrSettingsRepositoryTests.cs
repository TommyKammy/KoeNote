using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrSettingsRepositoryTests
{
    [Fact]
    public void SaveAndLoad_RestoresContextHotwordsEngineIdAndReviewToggle()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;
        var repository = new AsrSettingsRepository(paths);

        repository.Save(new AsrSettings("product meeting", "KoeNote\r\nRTX 3060,Whisper", "faster-whisper-large-v3-turbo", false));

        var restored = repository.Load();
        Assert.Equal("product meeting", restored.ContextText);
        Assert.Equal("KoeNote\r\nRTX 3060,Whisper", restored.HotwordsText);
        Assert.Equal("faster-whisper-large-v3-turbo", restored.EngineId);
        Assert.False(restored.EnableReviewStage);
        Assert.Equal(["KoeNote", "RTX 3060", "Whisper"], restored.Hotwords);
    }

    [Fact]
    public void Load_ReturnsDefaultSettingsWhenNotSaved()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;

        var restored = new AsrSettingsRepository(paths).Load();

        Assert.Equal("", restored.ContextText);
        Assert.Equal("", restored.HotwordsText);
        Assert.Equal("faster-whisper-large-v3-turbo", restored.EngineId);
        Assert.True(restored.EnableReviewStage);
        Assert.Empty(restored.Hotwords);
    }

}
