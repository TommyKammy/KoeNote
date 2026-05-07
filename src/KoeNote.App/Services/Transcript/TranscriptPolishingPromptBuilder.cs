using System.Text;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptPolishingPromptBuilder
{
    public const string PromptVersion = "polish-v1";

    public string Build(TranscriptPolishingChunk chunk)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var source = new StringBuilder();
        foreach (var segment in chunk.Segments)
        {
            source
                .Append("- segment_id: ").Append(segment.SegmentId).AppendLine()
                .Append("  timestamp: ").Append(FormatTimestamp(segment.StartSeconds)).AppendLine()
                .Append("  start_seconds: ").Append(segment.StartSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).AppendLine()
                .Append("  end_seconds: ").Append(segment.EndSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).AppendLine()
                .Append("  speaker: ").Append(segment.Speaker).AppendLine()
                .Append("  text: ").Append(segment.Text).AppendLine();
        }

        return $$"""
            You are polishing a Japanese ASR transcript for readability.

            Task:
            - Rewrite the transcript as readable prose.
            - Preserve speaker labels and segment order.
            - Preserve the meaning and intent of each speaker.
            - Add punctuation and paragraph breaks.
            - Remove fillers, repeated words, and obvious self-corrections only when they do not affect meaning.
            - Do not add facts that are not present in the transcript.
            - Do not guess uncertain names, numbers, dates, prices, decisions, owners, or deadlines.
            - Keep uncertain content cautious instead of making it confident.
            - Output plain text only. Do not output Markdown fences, explanations, or JSON.

            Output format:
            [HH:MM:SS] Speaker: polished utterance

            Source segments:
            {{source}}
            """;
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
