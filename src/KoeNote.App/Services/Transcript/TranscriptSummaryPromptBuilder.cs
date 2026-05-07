using System.Text;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptSummaryPromptBuilder
{
    public const string PromptVersion = "summary-v1";

    public string BuildChunkPrompt(TranscriptSummaryChunk chunk, string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        if (IsBonsaiModel(modelId))
        {
            return BuildBonsaiChunkPrompt(chunk);
        }

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

            Source transcript:
            {{chunk.Content}}
            """;
    }

    public string BuildFinalPrompt(IReadOnlyList<TranscriptSummaryChunkResult> chunkResults, string? modelId = null)
    {
        ArgumentNullException.ThrowIfNull(chunkResults);

        if (IsBonsaiModel(modelId))
        {
            return BuildBonsaiFinalPrompt(chunkResults);
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

            Chunk summaries:
            {{source}}
            """;
    }

    private string BuildBonsaiChunkPrompt(TranscriptSummaryChunk chunk)
    {
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

            文字起こし:
            {{chunk.Content}}
            """;
    }

    private string BuildBonsaiFinalPrompt(IReadOnlyList<TranscriptSummaryChunkResult> chunkResults)
    {
        var source = new StringBuilder();
        foreach (var result in chunkResults.OrderBy(static result => result.Chunk.ChunkIndex))
        {
            source
                .Append("### chunk ").Append(result.Chunk.ChunkIndex).AppendLine()
                .AppendLine(result.Content.Trim())
                .AppendLine();
        }

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

            チャンク要約:
            {{source}}
            """;
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
