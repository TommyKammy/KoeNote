using KoeNote.App.Services;
using KoeNote.App.Services.SystemStatus;

namespace KoeNote.App.Tests;

public sealed class StatusBarInfoServiceTests
{
    [Fact]
    public void FormatGpuUsage_FormatsNvidiaSmiCsvOutput()
    {
        var summary = StatusBarInfoService.FormatGpuUsage("12, 4096\r\n");

        Assert.Equal("GPU 12% / 4096 MB", summary);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\r\n")]
    public void FormatGpuUsage_ReturnsUnknownWhenOutputIsEmpty(string? output)
    {
        var summary = StatusBarInfoService.FormatGpuUsage(output);

        Assert.Equal("GPU Unknown", summary);
    }

    [Fact]
    public void GetStatusBarInfo_ReturnsAllSummaries()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root);
        paths.EnsureCreated();

        var info = new StatusBarInfoService(paths).GetStatusBarInfo();

        Assert.StartsWith("空き容量 ", info.DiskFreeSummary, StringComparison.Ordinal);
        Assert.StartsWith("MEM ", info.MemorySummary, StringComparison.Ordinal);
        Assert.StartsWith("CPU ", info.CpuSummary, StringComparison.Ordinal);
        Assert.StartsWith("GPU ", info.GpuUsageSummary, StringComparison.Ordinal);
    }
}
