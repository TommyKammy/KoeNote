using System.Text;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptSummaryPromptBuilder
{
    public const string PromptVersion = "summary-v1";

    public string BuildChunkPrompt(TranscriptSummaryChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);

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

    public string BuildFinalPrompt(IReadOnlyList<TranscriptSummaryChunkResult> chunkResults)
    {
        ArgumentNullException.ThrowIfNull(chunkResults);

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
