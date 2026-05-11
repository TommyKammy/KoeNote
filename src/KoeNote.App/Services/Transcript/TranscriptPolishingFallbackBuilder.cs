namespace KoeNote.App.Services.Transcript;

internal static class TranscriptPolishingFallbackBuilder
{
    public static bool IsChunkOutputUsable(TranscriptPolishingChunk chunk, string content, out string reason)
    {
        if (!TranscriptPolishingOutputNormalizer.IsUsableDocument(content, out reason))
        {
            return false;
        }

        var sourceLength = chunk.Segments.Sum(static segment => Math.Max(0, segment.Text.Length));
        if (sourceLength > 0 && content.Length > 2000 && content.Length > sourceLength * 6)
        {
            reason = "expanded far beyond the source chunk";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public static string BuildFallbackChunkContent(TranscriptPolishingChunk chunk)
    {
        var blocks = new List<string>();
        foreach (var speakerBlock in TranscriptPolishingChunkBuilder.BuildSpeakerBlocks(chunk.Segments))
        {
            var first = speakerBlock[0];
            var last = speakerBlock[^1];
            var text = string.Join(
                Environment.NewLine,
                speakerBlock
                    .Select(static segment => segment.Text.Trim())
                    .Where(static text => text.Length > 0));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            blocks.Add($"[{FormatTimestamp(first.StartSeconds)} - {FormatTimestamp(last.EndSeconds)}] {first.Speaker}: {text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    public static bool TryRecoverMissingTimestampContent(
        TranscriptPolishingChunk chunk,
        string content,
        out string recoveredContent)
    {
        recoveredContent = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || content.Contains('\uFFFD', StringComparison.Ordinal))
        {
            return false;
        }

        var sourceBlocks = TranscriptPolishingChunkBuilder.BuildSpeakerBlocks(chunk.Segments).ToArray();
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
        if (sourceBlocks.Length == 0 || lines.Length != sourceBlocks.Length)
        {
            return false;
        }

        var recoveredBlocks = new List<string>(sourceBlocks.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var sourceBlock = sourceBlocks[index];
            var first = sourceBlock[0];
            var last = sourceBlock[^1];
            var text = StripSpeakerPrefix(lines[index], first.Speaker).Trim();
            if (string.IsNullOrWhiteSpace(text) || LooksLikeGeneratedWrapper(text))
            {
                return false;
            }

            recoveredBlocks.Add($"[{FormatTimestamp(first.StartSeconds)} - {FormatTimestamp(last.EndSeconds)}] {first.Speaker}: {text}");
        }

        recoveredContent = string.Join(Environment.NewLine + Environment.NewLine, recoveredBlocks);
        return true;
    }

    private static string StripSpeakerPrefix(string line, string speaker)
    {
        var prefixes = new[]
        {
            $"{speaker}:",
            $"{speaker}\uFF1A"
        };

        foreach (var prefix in prefixes)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..];
            }
        }

        return line;
    }

    private static bool LooksLikeGeneratedWrapper(string text)
    {
        return text.StartsWith("BEGIN_BLOCK", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("END_BLOCK", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Output:", StringComparison.OrdinalIgnoreCase);
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
