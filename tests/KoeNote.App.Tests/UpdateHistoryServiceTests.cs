using KoeNote.App.Services;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateHistoryServiceTests
{
    [Fact]
    public void Record_AppendsJsonLinesAndReadRecentReturnsNewestEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var service = new UpdateHistoryService(paths);

        service.Record(new UpdateHistoryEntry(DateTimeOffset.Parse("2026-05-06T00:00:00+09:00"), "check_started", "Started."));
        service.Record(new UpdateHistoryEntry(
            DateTimeOffset.Parse("2026-05-06T00:01:00+09:00"),
            "download_verified",
            "Verified.",
            "0.14.0",
            Path.Combine(paths.UpdateDownloads, "KoeNote.msi"),
            new string('a', 64)));

        var entries = service.ReadRecent(1);

        Assert.True(File.Exists(paths.UpdateHistoryPath));
        Assert.Single(entries);
        Assert.Equal("download_verified", entries[0].EventName);
        Assert.Equal("0.14.0", entries[0].Version);
        Assert.Equal(new string('a', 64), entries[0].Sha256);
    }
}
