using System.Text.Json;
using KoeNote.App.Services;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateResultServiceTests
{
    [Fact]
    public void ConsumeLatestResult_ReturnsNewestResultAndMarksItSeen()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(paths.UpdateDownloads);
        var olderPath = Path.Combine(paths.UpdateDownloads, "older.result.json");
        var newerPath = Path.Combine(paths.UpdateDownloads, "newer.result.json");
        WriteResult(olderPath, "0.19.0", 0);
        WriteResult(newerPath, "0.20.0", 20);
        File.SetLastWriteTimeUtc(olderPath, DateTime.UtcNow.AddMinutes(-5));
        File.SetLastWriteTimeUtc(newerPath, DateTime.UtcNow);

        var result = new UpdateResultService(paths).ConsumeLatestResult();

        Assert.NotNull(result);
        Assert.Equal(newerPath, result.ResultPath);
        Assert.Equal("0.20.0", result.Version);
        Assert.Equal(20, result.ExitCode);
        Assert.False(File.Exists(newerPath));
        Assert.True(File.Exists(newerPath + ".seen"));
        Assert.True(File.Exists(olderPath));
    }

    private static void WriteResult(string path, string version, int exitCode)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            Status = exitCode == 0 ? "Success" : "InstallFailed",
            ExitCode = exitCode,
            Version = version,
            InstallerPath = "KoeNote.msi",
            TargetExePath = "KoeNote.App.exe",
            LogPath = "install.log",
            CompletedAt = DateTimeOffset.Parse("2026-06-18T00:00:00Z"),
            Message = exitCode == 0 ? "Update installed and KoeNote relaunched." : "msiexec exited with code 3010."
        }));
    }
}
