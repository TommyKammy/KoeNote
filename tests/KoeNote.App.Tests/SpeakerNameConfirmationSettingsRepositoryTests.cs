using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class SpeakerNameConfirmationSettingsRepositoryTests
{
    [Fact]
    public void Load_ReturnsDefaultWhenNotSaved()
    {
        var repository = new SpeakerNameConfirmationSettingsRepository(TestDatabase.CreateReadyPaths());

        var settings = repository.Load();

        Assert.Equal(SpeakerNameConfirmationModes.Always, settings.Mode);
    }

    [Fact]
    public void Save_PersistsConfirmationMode()
    {
        var repository = new SpeakerNameConfirmationSettingsRepository(TestDatabase.CreateReadyPaths());

        repository.Save(new SpeakerNameConfirmationSettings(SpeakerNameConfirmationModes.UnassignedOnly));

        Assert.Equal(SpeakerNameConfirmationModes.UnassignedOnly, repository.Load().Mode);
    }

    [Fact]
    public void Save_NormalizesUnsupportedModeToAlways()
    {
        var repository = new SpeakerNameConfirmationSettingsRepository(TestDatabase.CreateReadyPaths());

        repository.Save(new SpeakerNameConfirmationSettings("unsupported"));

        Assert.Equal(SpeakerNameConfirmationModes.Always, repository.Load().Mode);
    }
}
