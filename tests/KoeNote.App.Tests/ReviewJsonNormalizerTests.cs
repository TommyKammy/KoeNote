using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class ReviewJsonNormalizerTests
{
    [Fact]
    public void Normalize_ReadsCorrectionDrafts()
    {
        var segments = new[]
        {
            new TranscriptSegment("000012", "job-001", 0, 1, "Speaker_0", "この仕様はサーバーのミギワで処理します")
        };
        const string rawJson = """
            [
              {
                "segment_id": "000012",
                "issue_type": "意味不明語の疑い",
                "original_text": "この仕様はサーバーのミギワで処理します",
                "suggested_text": "この仕様はサーバーの右側で処理します",
                "reason": "文脈上「ミギワ」が不自然で、音の近い語として「右側」が候補になる。",
                "confidence": 0.62
              }
            ]
            """;

        var drafts = new ReviewJsonNormalizer().Normalize("job-001", segments, rawJson, minConfidence: 0.5);

        var draft = Assert.Single(drafts);
        Assert.Equal("000012", draft.SegmentId);
        Assert.Equal("意味不明語の疑い", draft.IssueType);
        Assert.Equal(0.62, draft.Confidence);
    }

    [Fact]
    public void Normalize_FiltersUnsupportedAdditions()
    {
        var segments = new[]
        {
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "はい。")
        };
        const string rawJson = """
            [
              {
                "segment_id": "000001",
                "issue_type": "情報追加",
                "original_text": "はい。",
                "suggested_text": "はい。2026年5月の営業会議で正式承認されました。",
                "reason": "文脈を補った。",
                "confidence": 0.99
              }
            ]
            """;

        var drafts = new ReviewJsonNormalizer().Normalize("job-001", segments, rawJson, minConfidence: 0.5);

        Assert.Empty(drafts);
    }

    [Fact]
    public void Normalize_ThrowsForInvalidJson()
    {
        var exception = Assert.Throws<ReviewWorkerException>(() =>
            new ReviewJsonNormalizer().Normalize("job-001", [], "not-json", minConfidence: 0.5));

        Assert.Equal(ReviewFailureCategory.JsonParseFailed, exception.Category);
    }

    [Fact]
    public void Normalize_IgnoresNonObjectArrayItems()
    {
        var drafts = new ReviewJsonNormalizer().Normalize("job-001", [], """["text-generation"]""", minConfidence: 0.5);

        Assert.Empty(drafts);
    }

    [Fact]
    public void Normalize_ProcessesTwentySegmentsWithNineteenUsableDrafts()
    {
        var segments = Enumerable.Range(1, 20)
            .Select(index => new TranscriptSegment(index.ToString("D6"), "job-001", index, index + 1, "Speaker_0", $"セグメント{index}のミギワです。"))
            .ToArray();
        var jsonItems = Enumerable.Range(1, 20)
            .Select(index => index == 20
                ? """{"segment_id":"000020","issue_type":"情報追加","original_text":"セグメント20のミギワです。","suggested_text":"セグメント20のミギワです。これは会議で正式承認された重要な内容です。","reason":"補足","confidence":0.99}"""
                : $$"""{"segment_id":"{{index.ToString("D6")}}","issue_type":"意味不明語の疑い","original_text":"セグメント{{index}}のミギワです。","suggested_text":"セグメント{{index}}の右側です。","reason":"音の近い語が候補になる。","confidence":0.80}""");
        var rawJson = $"[{string.Join(",", jsonItems)}]";

        var drafts = new ReviewJsonNormalizer().Normalize("job-001", segments, rawJson, minConfidence: 0.5);

        Assert.Equal(19, drafts.Count);
    }
}
