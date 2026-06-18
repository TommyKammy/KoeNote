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
        var oldLog = Path.Combine(paths.UpdateLogs, "old.log");
        var recentLog = Path.Combine(paths.UpdateLogs, "recent.log");
        var oldSeenResult = Path.Combine(paths.UpdateDownloads, "old.result.json.seen");
        var oldInvalidResult = Path.Combine(paths.UpdateDownloads, "old-invalid.result.json.invalid");
        var pendingResult = Path.Combine(paths.UpdateDownloads, "pending.result.json");
        File.WriteAllText(oldMsi, "old");
        File.WriteAllText(recentMsi, "recent");
        File.WriteAllText(oldTemp, "partial");
        File.WriteAllText(history, "{}");
        Directory.CreateDirectory(paths.UpdateLogs);
        File.WriteAllText(oldLog, "stale log");
        File.WriteAllText(recentLog, "fresh log");
        File.WriteAllText(oldSeenResult, "{}");
        File.WriteAllText(oldInvalidResult, "not-json");
        File.WriteAllText(pendingResult, "{}");
        var now = DateTimeOffset.Parse("2026-05-06T00:00:00+09:00");
        File.SetLastWriteTimeUtc(oldMsi, now.AddDays(-31).UtcDateTime);
        File.SetLastWriteTimeUtc(recentMsi, now.AddDays(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(oldTemp, now.AddDays(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(oldLog, now.AddDays(-31).UtcDateTime);
        File.SetLastWriteTimeUtc(recentLog, now.AddDays(-2).UtcDateTime);
        File.SetLastWriteTimeUtc(oldSeenResult, now.AddDays(-31).UtcDateTime);
        File.SetLastWriteTimeUtc(oldInvalidResult, now.AddDays(-31).UtcDateTime);
        File.SetLastWriteTimeUtc(pendingResult, now.AddDays(-31).UtcDateTime);
        var service = new UpdateDownloadCleanupService(paths, new UpdateDownloadCleanupOptions(
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(1),
            TimeSpan.FromDays(30),
            TimeSpan.FromDays(30)));

        var result = service.CleanupOldDownloads(now);

        Assert.Equal(1, result.DeletedVerifiedInstallers);
        Assert.Equal(1, result.DeletedTempFiles);
        Assert.Equal(1, result.DeletedUpdaterLogs);
        Assert.Equal(2, result.DeletedUpdaterResults);
        Assert.False(File.Exists(oldMsi));
        Assert.False(File.Exists(oldTemp));
        Assert.False(File.Exists(oldLog));
        Assert.False(File.Exists(oldSeenResult));
        Assert.False(File.Exists(oldInvalidResult));
        Assert.True(File.Exists(recentMsi));
        Assert.True(File.Exists(recentLog));
        Assert.True(File.Exists(pendingResult));
        Assert.True(File.Exists(history));
    }
}
