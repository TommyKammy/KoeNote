namespace KoeNote.App.Services.Transcript;

internal static class TranscriptPolishingFallbackBuilder
{
    public const string MissingTimestampReason = "missing timestamped speaker block";
    private const double TimestampToleranceSeconds = 1.0;

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

        if (!HasExpectedTranscriptBlocks(chunk, content, out reason))
        {
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
        if (sourceBlocks.Length == 0)
        {
            return false;
        }

        if (sourceBlocks.Length == 1)
        {
            return TryRecoverSingleMissingTimestampBlock(sourceBlocks[0], lines, out recoveredContent);
        }

        if (lines.Length != sourceBlocks.Length)
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

    private static bool TryRecoverSingleMissingTimestampBlock(
        IReadOnlyList<TranscriptReadModel> sourceBlock,
        IReadOnlyList<string> lines,
        out string recoveredContent)
    {
        recoveredContent = string.Empty;
        if (sourceBlock.Count == 0 || lines.Count == 0)
        {
            return false;
        }

        var first = sourceBlock[0];
        var last = sourceBlock[^1];
        var recoveredLines = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var text = StripSpeakerPrefix(line, first.Speaker).Trim();
            if (string.IsNullOrWhiteSpace(text) || LooksLikeGeneratedWrapper(text))
            {
                return false;
            }

            recoveredLines.Add(text);
        }

        var recoveredText = string.Join(Environment.NewLine + Environment.NewLine, recoveredLines).Trim();
        if (string.IsNullOrWhiteSpace(recoveredText))
        {
            return false;
        }

        recoveredContent = $"[{FormatTimestamp(first.StartSeconds)} - {FormatTimestamp(last.EndSeconds)}] {first.Speaker}: {recoveredText}";
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

    private static bool HasExpectedTranscriptBlocks(
        TranscriptPolishingChunk chunk,
        string content,
        out string reason)
    {
        var sourceBlocks = TranscriptPolishingChunkBuilder.BuildSpeakerBlocks(chunk.Segments).ToArray();
        var outputBlocks = ExtractTranscriptBlockRanges(content).ToArray();
        if (outputBlocks.Length != sourceBlocks.Length)
        {
            reason = $"unexpected transcript block count: expected {sourceBlocks.Length}, got {outputBlocks.Length}";
            return false;
        }

        for (var index = 0; index < outputBlocks.Length; index++)
        {
            var sourceBlock = sourceBlocks[index];
            var first = sourceBlock[0];
            var last = sourceBlock[^1];
            var outputBlock = outputBlocks[index];

            if (!string.Equals(outputBlock.Speaker, first.Speaker, StringComparison.Ordinal))
            {
                reason = $"unexpected speaker at block {index + 1}: expected {first.Speaker}, got {outputBlock.Speaker}";
                return false;
            }

            if (outputBlock.StartSeconds < first.StartSeconds - TimestampToleranceSeconds ||
                outputBlock.EndSeconds > last.EndSeconds + TimestampToleranceSeconds)
            {
                reason = $"timestamp outside source block at block {index + 1}";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

    private static IEnumerable<TranscriptBlockRange> ExtractTranscriptBlockRanges(string content)
    {
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!TryParseTranscriptBlockRange(trimmed, out var range))
            {
                continue;
            }

            yield return range;
        }
    }

    private static bool TryParseTranscriptBlockRange(string line, out TranscriptBlockRange range)
    {
        range = default;
        if (line.Length < 10 || line[0] != '[')
        {
            return false;
        }

        var closeBracket = line.IndexOf(']', StringComparison.Ordinal);
        if (closeBracket <= 0)
        {
            return false;
        }

        var timeRange = line[1..closeBracket];
        var separator = timeRange.IndexOf(" - ", StringComparison.Ordinal);
        if (separator <= 0)
        {
            return false;
        }

        if (!TryParseTimestamp(timeRange[..separator], out var startSeconds) ||
            !TryParseTimestamp(timeRange[(separator + 3)..], out var endSeconds))
        {
            return false;
        }

        if (endSeconds < startSeconds)
        {
            return false;
        }

        var rest = line[(closeBracket + 1)..].TrimStart();
        var speakerSeparator = rest.IndexOf(':', StringComparison.Ordinal);
        if (speakerSeparator < 0)
        {
            speakerSeparator = rest.IndexOf('\uFF1A', StringComparison.Ordinal);
        }

        if (speakerSeparator <= 0)
        {
            return false;
        }

        var speaker = rest[..speakerSeparator].Trim();
        if (speaker.Length == 0)
        {
            return false;
        }

        range = new TranscriptBlockRange(startSeconds, endSeconds, speaker);
        return true;
    }

    private static bool TryParseTimestamp(string value, out double seconds)
    {
        seconds = 0;
        var parts = value.Trim().Split(':');
        if (parts.Length is < 2 or > 3)
        {
            return false;
        }

        if (!double.TryParse(
                parts[^1],
                System.Globalization.NumberStyles.AllowDecimalPoint,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedSeconds) ||
            !int.TryParse(
                parts[^2],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out var minutes))
        {
            return false;
        }

        var hours = 0;
        if (parts.Length == 3 &&
            !int.TryParse(
                parts[0],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out hours))
        {
            return false;
        }

        seconds = (hours * 3600) + (minutes * 60) + parsedSeconds;
        return true;
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private readonly record struct TranscriptBlockRange(double StartSeconds, double EndSeconds, string Speaker);
}
