using KoeNote.App.Services;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrSettingsRepositoryTests
{
    [Fact]
    public void SaveAndLoad_RestoresContextAndHotwords()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var repository = new AsrSettingsRepository(paths);

        repository.Save(new AsrSettings("製品開発会議", "KoeNote\r\nRTX 3060,Whisper"));

        var restored = repository.Load();
        Assert.Equal("製品開発会議", restored.ContextText);
        Assert.Equal("KoeNote\r\nRTX 3060,Whisper", restored.HotwordsText);
        Assert.Equal(["KoeNote", "RTX 3060", "Whisper"], restored.Hotwords);
    }

    [Fact]
    public void Load_ReturnsEmptySettingsWhenNotSaved()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var restored = new AsrSettingsRepository(paths).Load();

        Assert.Equal("", restored.ContextText);
        Assert.Equal("", restored.HotwordsText);
        Assert.Empty(restored.Hotwords);
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, root, AppContext.BaseDirectory);
    }
}
