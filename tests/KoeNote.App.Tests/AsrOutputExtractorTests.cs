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

    [Fact]
    public void ExtractJson_SkipsModelMetadataArraysFromStderr()
    {
        var json = AsrOutputExtractor.ExtractJson("""
            <|channel|> final<|message|> [{"segment_id":"000001"}] [end of text]
            """, """
            llama_model_loader: general.tags arr[str,1] = ["text-generation"]
            """);

        Assert.Equal("""[{"segment_id":"000001"}]""", json);
    }

    [Fact]
    public void ExtractJson_DoesNotTreatScalarArrayAsDomainJson()
    {
        var json = AsrOutputExtractor.ExtractJson("""["text-generation"]""", "");

        Assert.Equal("""["text-generation"]""", json);
    }
}
