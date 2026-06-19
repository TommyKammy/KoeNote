using System.Text;

namespace KoeNote.App.Services.Transcript;

internal static class TranscriptSummaryFallbackBuilder
{
    public static string BuildSummary(
        IReadOnlyList<TranscriptReadModel> segments,
        IReadOnlyList<TranscriptSummaryChunkResult>? chunkResults = null)
    {
        var usableChunkResults = chunkResults?
            .Where(static result => !string.IsNullOrWhiteSpace(result.Content))
            .OrderBy(static result => result.Chunk.ChunkIndex)
            .ToArray();
        if (usableChunkResults is { Length: > 0 })
        {
            return BuildStructuredChunkFallbackSummary(segments, usableChunkResults);
        }

        var segmentFallbackBullets = SummaryBulletParser.BuildSegmentFallbackBullets(segments, 8);
        var fallbackActions = BuildFallbackActionItems([], segmentFallbackBullets);
        var fallbackKeywords = SummaryKeywordExtractor.BuildFallbackKeywords([], segmentFallbackBullets, segmentFallbackBullets);

        var builder = new StringBuilder();
        builder
            .AppendLine("## Overview")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, segmentFallbackBullets.Take(4));
        builder
            .AppendLine()
            .AppendLine("## Key points")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, segmentFallbackBullets);

        builder
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine()
            .AppendLine("- 明示された決定事項はありません。")
            .AppendLine()
            .AppendLine("## Action items")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, fallbackActions);
        builder
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine()
            .AppendLine("- 明示された未解決事項はありません。")
            .AppendLine()
            .AppendLine("## Keywords")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, fallbackKeywords);

        return builder.ToString().Trim();
    }

    public static string BuildChunkSummary(TranscriptSummaryChunk chunk)
    {
        return $"""
            ## 概要

            LLM 要約を利用できなかったため、このチャンクは文字起こしの抜粋として保存しました。

            ## 主な内容

            {SummaryTextNormalizer.TrimForSummary(chunk.Content, 1000)}
            """;
    }

    public static string BuildSegmentRange(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments.Count == 0 ? string.Empty : $"{segments[0].SegmentId}..{segments[^1].SegmentId}";
    }

    public static string BuildChunkId(string derivativeId, TranscriptSummaryChunk chunk)
    {
        return $"{derivativeId}-chunk-{chunk.ChunkIndex:D3}";
    }

    public static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static string BuildStructuredChunkFallbackSummary(
        IReadOnlyList<TranscriptReadModel> segments,
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults)
    {
        var overviews = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Overview", "概要"], 4);
        var keyPoints = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Key points", "主な内容"], 9);
        var decisions = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Decisions", "決定事項"], 4);
        var actions = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Action items", "アクション項目"], 6);
        var openQuestions = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Open questions", "未解決の質問"], 4);
        var segmentFallbackBullets = SummaryBulletParser.BuildSegmentFallbackBullets(segments, 8);
        var keywords = SummaryKeywordExtractor.NormalizeKeywordBullets(
            SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Keywords", "キーワード"], 10),
            overviews.Concat(keyPoints).Concat(segmentFallbackBullets));
        var fallbackActions = actions.Length > 0
            ? actions
            : BuildFallbackActionItems(keyPoints, segmentFallbackBullets);

        var builder = new StringBuilder();
        builder
            .AppendLine("## Overview")
            .AppendLine();

        SummaryBulletParser.AppendBullets(builder, overviews.Length > 0
            ? overviews
            : keyPoints.Length > 0
                ? keyPoints.Take(4)
                : segmentFallbackBullets.Take(4));
        builder
            .AppendLine()
            .AppendLine("## Key points")
            .AppendLine();

        if (keyPoints.Length == 0)
        {
            keyPoints = segmentFallbackBullets;
        }

        SummaryBulletParser.AppendBullets(builder, keyPoints);
        builder
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, decisions.Length > 0 ? decisions : ["明示された決定事項はありません。"]);

        builder
            .AppendLine()
            .AppendLine("## Action items")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, fallbackActions);

        builder
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, openQuestions.Length > 0 ? openQuestions : ["明示された未解決事項はありません。"]);

        builder
            .AppendLine()
            .AppendLine("## Keywords")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, keywords.Length > 0
            ? keywords
            : SummaryKeywordExtractor.BuildFallbackKeywords(overviews, keyPoints, segmentFallbackBullets));

        return builder.ToString().Trim();
    }

    private static string[] BuildFallbackActionItems(
        IReadOnlyList<string> keyPoints,
        IReadOnlyList<string> segmentFallbackBullets)
    {
        var actions = keyPoints
            .Concat(segmentFallbackBullets)
            .Where(ContainsActionCue)
            .Take(6)
            .ToArray();
        return actions.Length > 0 ? actions : ["明示されたアクション項目はありません。"];
    }

    private static bool ContainsActionCue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] cues =
        [
            "必要",
            "推奨",
            "活用",
            "意識",
            "調べ",
            "確認",
            "準備",
            "取り組",
            "話し合",
            "確保",
            "使う",
            "作る",
            "plan",
            "prepare",
            "confirm",
            "review",
            "use",
            "follow"
        ];
        return cues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }
}
