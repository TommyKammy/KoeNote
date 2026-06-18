using System.Text;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class TranscriptSummaryTextHelperTests
{
    [Fact]
    public void NormalizeUserFacingSummary_SplitsKeywordsAndStripsSourceReferences()
    {
        var normalized = SummaryTextNormalizer.NormalizeUserFacingSummary("""
            ## Keywords

            - **CUDA** (segment_id: 1), 要約、CUDA

            ## Decisions

            Unspecified
            """);

        Assert.Contains("- CUDA", normalized);
        Assert.Contains("- 要約", normalized);
        Assert.DoesNotContain("segment_id", normalized, StringComparison.Ordinal);
        Assert.Contains("- Unspecified.", normalized);
    }

    [Fact]
    public void NormalizeKeywordBullets_RemovesSourceSentencesAndSentenceLikeCandidates()
    {
        var keywords = SummaryKeywordExtractor.NormalizeKeywordBullets(
            ["**CUDA** (segment_id: 1)、これは文章です、要約"],
            ["CUDA を使います"]);

        Assert.Equal(["CUDA", "要約"], keywords);
    }

    [Fact]
    public void AppendBullets_DeduplicatesAndStripsReferences()
    {
        var builder = new StringBuilder();

        SummaryBulletParser.AppendBullets(builder, ["決定事項です。 [1]", "決定事項です", "**次回確認**"]);

        Assert.Equal(
            ["- 決定事項です。", "- 次回確認"],
            builder.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }
}
