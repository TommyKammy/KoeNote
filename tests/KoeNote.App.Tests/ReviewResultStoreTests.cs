using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewResultStoreTests
{
    [Fact]
    public void Save_WritesRawAndNormalizedDrafts()
    {
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var store = new ReviewResultStore();

        var rawPath = store.SaveRawOutput(output, """[{"segment_id":"000001","reason":"テスト"}]""");
        var normalizedPath = store.SaveNormalizedDrafts(output, [
            new CorrectionDraft("000001-01", "job-001", "000001", "表記ゆれ", "API", "API", "テスト", 0.5)
        ]);

        Assert.True(File.Exists(rawPath));
        Assert.True(File.Exists(normalizedPath));
        Assert.Contains("テスト", File.ReadAllText(rawPath));
        Assert.Contains("表記ゆれ", File.ReadAllText(normalizedPath));
    }
}
