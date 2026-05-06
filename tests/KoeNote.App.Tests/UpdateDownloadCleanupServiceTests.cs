using KoeNote.App.Services;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateDownloadCleanupServiceTests
{
    [Fact]
    public void CleanupOldDownloads_RemovesExpiredMsiAndTempFilesOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(paths.UpdateDownloads);
        var oldMsi = Path.Combine(paths.UpdateDownloads, "old.msi");
        var recentMsi = Path.Combine(paths.UpdateDownloads, "recent.msi");
        var oldTemp = Path.Combine(paths.UpdateDownloads, "old.msi.download");
        var history = Path.Combine(paths.UpdateDownloads, "history.jsonl");
        File.WriteAllText(oldMsi, "old");
        File.WriteAllText(recentMsi, "recent");
        File.WriteAllText(oldTemp, "partial");
        File.WriteAllText(history, "{}");
        var now = DateTimeOffset.Parse("2026-05-06T00:00:00+09:00");
        File.SetLastWriteTimeUtc(oldMsi, now.AddDays(-31).UtcDateTime);
        File.SetLastWriteTimeUtc(recentMsi, now.AddDays(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(oldTemp, now.AddDays(-2).UtcDateTime);
        var service = new UpdateDownloadCleanupService(paths, new UpdateDownloadCleanupOptions(TimeSpan.FromDays(30), TimeSpan.FromDays(1)));

        var result = service.CleanupOldDownloads(now);

        Assert.Equal(1, result.DeletedVerifiedInstallers);
        Assert.Equal(1, result.DeletedTempFiles);
        Assert.False(File.Exists(oldMsi));
        Assert.False(File.Exists(oldTemp));
        Assert.True(File.Exists(recentMsi));
        Assert.True(File.Exists(history));
    }
}
