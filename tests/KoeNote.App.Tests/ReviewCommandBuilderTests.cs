using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewCommandBuilderTests
{
    [Fact]
    public void BuildArgumentList_IncludesModelPromptFileGpuAndNonInteractiveSettings()
    {
        var options = new ReviewRunOptions(
            "job-001",
            "llama-completion.exe",
            @"C:\models\llm-jp.gguf",
            @"C:\out",
            [new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "テストです。")]);

        var arguments = new ReviewCommandBuilder().BuildArgumentList(
            options,
            @"C:\out\review.prompt.txt",
            @"C:\out\review.schema.json");

        Assert.Equal("--model", arguments[0]);
        Assert.Equal(@"C:\models\llm-jp.gguf", arguments[1]);
        Assert.Contains("--file", arguments);
        Assert.Contains(@"C:\out\review.prompt.txt", arguments);
        Assert.Contains("--temp", arguments);
        Assert.Contains("0.1", arguments);
        Assert.Contains("16384", arguments);
        Assert.Contains("4096", arguments);
        Assert.Contains("--n-gpu-layers", arguments);
        Assert.Contains("999", arguments);
        Assert.Contains("--single-turn", arguments);
        Assert.Contains("--no-display-prompt", arguments);
        Assert.Contains("--json-schema-file", arguments);
        Assert.Contains(@"C:\out\review.schema.json", arguments);
    }
}
