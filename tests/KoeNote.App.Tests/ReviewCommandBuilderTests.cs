using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewCommandBuilderTests
{
    [Fact]
    public void BuildPrompt_UsesReadableJapaneseInstructions()
    {
        var prompt = new ReviewPromptBuilder().Build([
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "今日はテストです")
        ]);

        Assert.Contains("日本語ASR結果の校正レビュー担当", prompt, StringComparison.Ordinal);
        Assert.Contains("出力はJSONのみ", prompt, StringComparison.Ordinal);
        Assert.Contains("ASRセグメント:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("縺", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("繝", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRepairPrompt_UsesReadableJapaneseInstructions()
    {
        var prompt = new ReviewPromptBuilder().BuildRepairPrompt("not json");

        Assert.Contains("JSON配列だけに修復", prompt, StringComparison.Ordinal);
        Assert.Contains("修復対象:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("縺", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("繝", prompt, StringComparison.Ordinal);
    }

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
        Assert.Contains("8192", arguments);
        Assert.Contains("4096", arguments);
        Assert.Contains("--n-gpu-layers", arguments);
        Assert.Contains("999", arguments);
        Assert.Contains("--single-turn", arguments);
        Assert.Contains("--no-display-prompt", arguments);
        Assert.Contains("--json-schema-file", arguments);
        Assert.Contains(@"C:\out\review.schema.json", arguments);
    }

    [Fact]
    public void BuildArgumentList_UsesRuntimeTuningOverrides()
    {
        var options = new ReviewRunOptions(
            "job-001",
            "llama-completion.exe",
            @"C:\models\ternary.gguf",
            @"C:\out",
            [new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text")],
            ContextSize: 1024,
            GpuLayers: 0,
            MaxTokens: 192,
            Threads: 8,
            ThreadsBatch: 8,
            UseJsonSchema: false);

        var arguments = new ReviewCommandBuilder().BuildArgumentList(
            options,
            @"C:\out\review.prompt.txt",
            options.UseJsonSchema ? @"C:\out\review.schema.json" : null);

        Assert.Contains("1024", arguments);
        Assert.Contains("192", arguments);
        Assert.Contains("--threads", arguments);
        Assert.Contains("8", arguments);
        Assert.Contains("--threads-batch", arguments);
        Assert.DoesNotContain("--json-schema-file", arguments);
    }
}
