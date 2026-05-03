using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewCommandBuilderTests
{
    [Fact]
    public void BuildArgumentList_IncludesModelPromptAndDeterministicSettings()
    {
        var options = new ReviewRunOptions(
            "job-001",
            "llama-completion.exe",
            @"C:\models\llm-jp.gguf",
            @"C:\out",
            [new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "テストです。")]);

        var arguments = new ReviewCommandBuilder().BuildArgumentList(options, "prompt text");

        Assert.Equal("--model", arguments[0]);
        Assert.Equal(@"C:\models\llm-jp.gguf", arguments[1]);
        Assert.Contains("--prompt", arguments);
        Assert.Contains("prompt text", arguments);
        Assert.Contains("--temp", arguments);
        Assert.Contains("0.1", arguments);
    }
}
