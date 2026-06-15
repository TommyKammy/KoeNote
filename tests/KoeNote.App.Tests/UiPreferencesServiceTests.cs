using KoeNote.App.Services;

namespace KoeNote.App.Tests;

public sealed class UiPreferencesServiceTests
{
    [Fact]
    public void Load_DefaultsToStandardLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var service = new UiPreferencesService(new AppPaths(root, root, AppContext.BaseDirectory));

        var preferences = service.Load();

        Assert.Equal(1.0, preferences.MainContentZoomScale);
        Assert.Equal(MainLayoutMode.Standard, preferences.MainLayoutMode);
    }

    [Fact]
    public void SaveSpecificPreferences_PreserveOtherValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var service = new UiPreferencesService(new AppPaths(root, root, AppContext.BaseDirectory));

        service.SaveMainLayoutMode(MainLayoutMode.Detail);
        service.SaveMainContentZoomScale(1.25);

        var preferences = service.Load();
        Assert.Equal(MainLayoutMode.Detail, preferences.MainLayoutMode);
        Assert.Equal(1.25, preferences.MainContentZoomScale);

        service.SaveMainLayoutMode(MainLayoutMode.Standard);

        preferences = service.Load();
        Assert.Equal(MainLayoutMode.Standard, preferences.MainLayoutMode);
        Assert.Equal(1.25, preferences.MainContentZoomScale);
    }
}
