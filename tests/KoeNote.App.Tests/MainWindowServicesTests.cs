using KoeNote.App.Services;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class MainWindowServicesTests
{
    [Fact]
    public void Create_InitializesDatabaseAndCoreRegistries()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);

        var services = MainWindowServices.Create(paths);

        Assert.True(File.Exists(paths.DatabasePath));
        Assert.True(services.AsrEngineRegistry.Contains("vibevoice-crispasr"));
        Assert.True(services.AsrEngineRegistry.Contains("kotoba-whisper-v2.2-faster"));
        Assert.True(services.AsrEngineRegistry.Contains("faster-whisper-large-v3-turbo"));
        Assert.True(services.AsrEngineRegistry.Contains("faster-whisper-large-v3"));
        Assert.True(services.AsrEngineRegistry.Contains("reazonspeech-k2-v3"));
        Assert.NotNull(services.AudioPlaybackService);
        Assert.NotNull(services.ModelDownloadJobRepository);
        Assert.NotNull(services.ModelDownloadService);
        Assert.NotNull(services.DatabaseMaintenanceService);
        Assert.NotEmpty(services.ToolStatusService.GetStatusItems());
    }
}
