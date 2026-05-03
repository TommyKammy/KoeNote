using System.Text.Json;
using KoeNote.App.Models;
using KoeNote.EvalBench;

namespace KoeNote.App.Tests;

public sealed class EvaluationBenchTests
{
    [Fact]
    public void CharacterErrorRate_ComputesEditDistanceOverReferenceLength()
    {
        var rate = TextMetrics.CharacterErrorRate("KoeNote", "KoeNoto");

        Assert.Equal(1 / 7.0, rate, precision: 6);
    }

    [Fact]
    public void Runner_WritesReportAndLatestJson()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var report = new EvaluationBenchRunner().Run(new EvaluationBenchOptions(outputRoot));

        Assert.Equal("passed", report.Summary.Status);
        Assert.True(File.Exists(report.ReportPath));
        Assert.True(File.Exists(Path.Combine(outputRoot, "latest.json")));
        Assert.True(report.Summary.AsrCharacterErrorRate <= 0.25);
        Assert.Equal(0, report.Summary.ReviewJsonParseFailureRate);
        Assert.True(report.Summary.MemorySuggestionCount > 0);

        using var document = JsonDocument.Parse(File.ReadAllText(report.ReportPath));
        Assert.True(document.RootElement.TryGetProperty("Summary", out _));
        Assert.True(document.RootElement.TryGetProperty("Host", out _));
    }

    [Fact]
    public void Runner_CreatesDistinctReportPathsForBackToBackRuns()
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var runner = new EvaluationBenchRunner();

        var first = runner.Run(new EvaluationBenchOptions(outputRoot));
        var second = runner.Run(new EvaluationBenchOptions(outputRoot));

        Assert.NotEqual(first.ReportPath, second.ReportPath);
        Assert.True(File.Exists(first.ReportPath));
        Assert.True(File.Exists(second.ReportPath));
    }

    [Fact]
    public void EvaluateReviewJson_ReturnsParseFailureMetricInsteadOfThrowing()
    {
        var result = EvaluationBenchRunner.EvaluateReviewJson(
            "job-001",
            [new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text")],
            "not-json");

        Assert.True(result.JsonParseFailed);
        Assert.Equal(0, result.DraftCount);
    }
}
