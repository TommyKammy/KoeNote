using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrOutputExtractorTests
{
    [Fact]
    public void ExtractJson_ReturnsStdoutJson()
    {
        var json = AsrOutputExtractor.ExtractJson("""{"segments":[]}""", "warning");

        Assert.Equal("""{"segments":[]}""", json);
    }

    [Fact]
    public void ExtractJson_PullsJsonFromStdoutLogs()
    {
        var json = AsrOutputExtractor.ExtractJson("""
            loading model...
            {"segments":[{"text":"テストです。"}]}
            done
            """, "");

        Assert.Equal("""{"segments":[{"text":"テストです。"}]}""", json);
    }

    [Fact]
    public void ExtractJson_FallsBackToStderrJson()
    {
        var json = AsrOutputExtractor.ExtractJson("", """
            warning
            [{"text":"stderr json"}]
            """);

        Assert.Equal("""[{"text":"stderr json"}]""", json);
    }

    [Fact]
    public void ExtractJson_SkipsBracketedLogLines()
    {
        var json = AsrOutputExtractor.ExtractJson("""
            [info] loading model
            {"segments":[{"text":"actual json"}]}
            """, "");

        Assert.Equal("""{"segments":[{"text":"actual json"}]}""", json);
    }
}
