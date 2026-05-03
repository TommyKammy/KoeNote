using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrResultStoreTests
{
    [Fact]
    public void Save_WritesRawAndNormalizedJson()
    {
        var output = CreateTempDirectory();
        var segments = new[]
        {
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "テストです。", "テストです。")
        };

        var paths = new AsrResultStore().Save(output, """{"segments":[]}""", segments);

        Assert.True(File.Exists(paths.RawOutputPath));
        Assert.True(File.Exists(paths.NormalizedSegmentsPath));
        Assert.Contains("テストです。", File.ReadAllText(paths.NormalizedSegmentsPath));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
