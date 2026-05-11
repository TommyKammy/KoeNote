using System.Globalization;
using System.Text;

namespace KoeNote.App.Services.Transcript;

internal static class TranscriptPolishingPromptSourceBuilder
{
    public static string Build(IReadOnlyList<TranscriptReadModel> segments)
    {
        var source = new StringBuilder();
        foreach (var block in BuildSpeakerBlocks(segments))
        {
            source
                .Append("- speaker_block_id: ").Append(block.BlockId).AppendLine()
                .Append("  source_segment_ids: ").Append(string.Join(",", block.Segments.Select(static segment => segment.SegmentId))).AppendLine()
                .Append("  timestamp: ").Append(FormatTimestamp(block.StartSeconds)).Append(" - ").Append(FormatTimestamp(block.EndSeconds)).AppendLine()
                .Append("  start_seconds: ").Append(block.StartSeconds.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine()
                .Append("  end_seconds: ").Append(block.EndSeconds.ToString("0.###", CultureInfo.InvariantCulture)).AppendLine()
                .Append("  speaker: ").Append(block.Speaker).AppendLine()
                .AppendLine("  combined_text: |");
            foreach (var line in BuildCombinedTextLines(block.Segments))
            {
                source.Append("    ").AppendLine(line);
            }
        }

        return source.ToString();
    }

    private static IReadOnlyList<SpeakerBlock> BuildSpeakerBlocks(IReadOnlyList<TranscriptReadModel> segments)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        var blocks = new List<SpeakerBlock>();
        var current = new List<TranscriptReadModel>();
        var currentSpeaker = string.Empty;

        foreach (var segment in segments)
        {
            if (current.Count > 0 && !string.Equals(currentSpeaker, segment.Speaker, StringComparison.Ordinal))
            {
                blocks.Add(CreateSpeakerBlock(blocks.Count + 1, current));
                current = [];
            }

            currentSpeaker = segment.Speaker;
            current.Add(segment);
        }

        if (current.Count > 0)
        {
            blocks.Add(CreateSpeakerBlock(blocks.Count + 1, current));
        }

        return blocks;
    }

    private static SpeakerBlock CreateSpeakerBlock(int index, IReadOnlyList<TranscriptReadModel> segments)
    {
        return new SpeakerBlock(
            $"block-{index:D3}",
            segments[0].Speaker,
            segments[0].StartSeconds,
            segments[^1].EndSeconds,
            segments);
    }

    private static IEnumerable<string> BuildCombinedTextLines(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments
            .Select(static segment => segment.Text.Trim())
            .Where(static text => text.Length > 0);
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private sealed record SpeakerBlock(
        string BlockId,
        string Speaker,
        double StartSeconds,
        double EndSeconds,
        IReadOnlyList<TranscriptReadModel> Segments);
}
