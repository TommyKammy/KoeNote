using System.Text;
using KoeNote.App.Services.Presets;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptSummaryPromptBuilder
{
    public const string PromptVersion = "summary-v1";

    public string BuildChunkPrompt(
        TranscriptSummaryChunk chunk,
        string? modelId = null,
        DomainPromptContext? domainContext = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (IsBonsaiModel(modelId))
        {
            return BuildBonsaiChunkPrompt(chunk, domainContext);
        }

        var domainBlock = BuildDomainContextBlock(domainContext, isBonsai: false);
        return $$"""
            You are summarizing a Japanese meeting or interview transcript chunk.

            Task:
            - Extract only information that is present in the source transcript.
            - Do not add facts that are not present in the transcript.
            - Do not invent participants, owners, dates, deadlines, decisions, or action items.
            - Use "Unspecified" when owner, date, participant, or deadline is not present.
            - Include decisions only when the source states or clearly implies a decision.
            - Include action items only when the source implies an action.
            - Preserve source references by mentioning segment ids or timestamps for important decisions and action items.
            - Output Markdown only. Do not output code fences.
            - Do not output analysis, reasoning, or <think> blocks.
            - Do not repeat or quote these instructions.
            - If the source transcript is Japanese, write the summary in Japanese.
            - Begin with the Overview section heading.

            Output sections:
            ## Overview
            ## Key points
            ## Decisions
            ## Action items
            ## Open questions
            ## Keywords

            Source kind: {{chunk.SourceKind}}
            Source segment ids: {{chunk.SourceSegmentIds}}
            Source time range: {{FormatRange(chunk.SourceStartSeconds, chunk.SourceEndSeconds)}}
            {{domainBlock}}

            Source transcript:
            {{chunk.Content}}
            """;
    }

    public string BuildFinalPrompt(
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        string? modelId = null,
        DomainPromptContext? domainContext = null)
    {
        ArgumentNullException.ThrowIfNull(chunkResults);

        if (IsBonsaiModel(modelId))
        {
            return BuildBonsaiFinalPrompt(chunkResults, domainContext);
        }

        var source = new StringBuilder();
        foreach (var result in chunkResults.OrderBy(static result => result.Chunk.ChunkIndex))
        {
            source
                .Append("### Chunk ").Append(result.Chunk.ChunkIndex).AppendLine()
                .Append("Source segment ids: ").Append(result.Chunk.SourceSegmentIds).AppendLine()
                .Append("Source time range: ").Append(FormatRange(result.Chunk.SourceStartSeconds, result.Chunk.SourceEndSeconds)).AppendLine()
                .AppendLine(result.Content.Trim())
                .AppendLine();
        }

        var domainBlock = BuildDomainContextBlock(domainContext, isBonsai: false);
        return $$"""
            You are merging transcript chunk summaries into one final Japanese summary.

            Task:
            - Merge overlapping points and remove duplication.
            - Keep only information that is present in the chunk summaries.
            - Do not add facts that are not present in the transcript or chunk summaries.
            - Do not invent participants, owners, dates, deadlines, decisions, or action items.
            - Use "Unspecified" when owner, date, participant, or deadline is not present.
            - Include decisions only when the source states or clearly implies a decision.
            - Include action items only when the source implies an action.
            - Keep source references for important decisions and action items when available.
            - Output Markdown only. Do not output code fences.
            - Do not output analysis, reasoning, or <think> blocks.
            - Do not repeat or quote these instructions.
            - If the chunk summaries are Japanese, write the final summary in Japanese.
            - Begin with the Overview section heading.

            Output sections in this exact order:
            ## Overview
            ## Key points
            ## Decisions
            ## Action items
            ## Open questions
            ## Keywords
            {{domainBlock}}

            Chunk summaries:
            {{source}}
            """;
    }

    private string BuildBonsaiChunkPrompt(TranscriptSummaryChunk chunk, DomainPromptContext? domainContext)
    {
        var domainBlock = BuildDomainContextBlock(domainContext, isBonsai: true);
        return $$"""
            /no_think
            日本語で短く要約してください。思考、説明、前置き、英語は禁止です。
            出力はMarkdownのみ。必ず次の見出しだけをこの順番で使ってください。

            ## 概要
            ## 主な内容
            ## 決定事項
            ## アクション項目
            ## 未解決の質問
            ## キーワード

            ルール:
            - 文字起こしにない内容は追加しない。
            - 決定事項やアクション項目がない場合は「なし」と書く。
            - 各セクションは最大3項目。
            - 重要項目には segment_id または時刻を書く。
            - <think>、分析、手順説明、指示文の引用は禁止。

            範囲: {{FormatRange(chunk.SourceStartSeconds, chunk.SourceEndSeconds)}}
            segment_ids: {{chunk.SourceSegmentIds}}
            {{domainBlock}}

            文字起こし:
            {{chunk.Content}}
            """;
    }

    private string BuildBonsaiFinalPrompt(
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        DomainPromptContext? domainContext)
    {
        var source = new StringBuilder();
        foreach (var result in chunkResults.OrderBy(static result => result.Chunk.ChunkIndex))
        {
            source
                .Append("### chunk ").Append(result.Chunk.ChunkIndex).AppendLine()
                .AppendLine(result.Content.Trim())
                .AppendLine();
        }

        var domainBlock = BuildDomainContextBlock(domainContext, isBonsai: true);
        return $$"""
            /no_think
            日本語のチャンク要約を、重複を除いて短く統合してください。思考、説明、前置き、英語は禁止です。
            出力はMarkdownのみ。必ず次の見出しだけをこの順番で使ってください。

            ## 概要
            ## 主な内容
            ## 決定事項
            ## アクション項目
            ## 未解決の質問
            ## キーワード

            ルール:
            - チャンク要約にない内容は追加しない。
            - 決定事項やアクション項目がない場合は「なし」と書く。
            - 各セクションは最大3項目。
            - <think>、分析、手順説明、指示文の引用は禁止。
            {{domainBlock}}

            チャンク要約:
            {{source}}
            """;
    }

    private static string BuildDomainContextBlock(DomainPromptContext? domainContext, bool isBonsai)
    {
        if (domainContext is null || domainContext.IsEmpty)
        {
            return string.Empty;
        }

        var limits = isBonsai
            ? DomainPromptContextLimits.BonsaiSummary
            : DomainPromptContextLimits.SummaryDefault;
        var terms = domainContext.Terms
            .Select(static term => term.Surface.Trim())
            .Where(static term => term.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(limits.TermLimit)
            .ToArray();
        var pairs = domainContext.CorrectionPairs
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.WrongText) && !string.IsNullOrWhiteSpace(pair.CorrectText))
            .Take(limits.CorrectionPairLimit)
            .ToArray();
        var guidelines = domainContext.ReviewGuidelines
            .Select(static guideline => guideline.Trim())
            .Where(static guideline => guideline.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(limits.ReviewGuidelineLimit)
            .ToArray();

        if (terms.Length == 0 && pairs.Length == 0 && guidelines.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.AppendLine();
        builder.AppendLine(isBonsai ? "辞書ヒント:" : "Domain dictionary hints:");
        builder.AppendLine(isBonsai
            ? "- 文脈に合う場合だけ使う。文字起こしにない新情報は追加しない。"
            : "- Use these hints only when they match the source transcript context. Do not add facts that are not in the source.");

        if (terms.Length > 0)
        {
            builder.AppendLine(isBonsai ? "- 専門語彙:" : "- Terms:");
            foreach (var term in terms)
            {
                builder.Append("  - ").AppendLine(term);
            }
        }

        if (pairs.Length > 0)
        {
            builder.AppendLine(isBonsai ? "- ASR誤認識候補:" : "- ASR correction candidates:");
            foreach (var pair in pairs)
            {
                builder
                    .Append("  - ")
                    .Append(pair.WrongText.Trim())
                    .Append(" -> ")
                    .AppendLine(pair.CorrectText.Trim());
            }
        }

        if (guidelines.Length > 0)
        {
            builder.AppendLine(isBonsai ? "- 領域指針:" : "- Domain guidelines:");
            foreach (var guideline in guidelines)
            {
                builder.Append("  - ").AppendLine(guideline);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsBonsaiModel(string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            modelId.Contains("bonsai", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRange(double? startSeconds, double? endSeconds)
    {
        if (startSeconds is null || endSeconds is null)
        {
            return "Unspecified";
        }

        return $"{FormatTimestamp(startSeconds.Value)}..{FormatTimestamp(endSeconds.Value)}";
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }
}
