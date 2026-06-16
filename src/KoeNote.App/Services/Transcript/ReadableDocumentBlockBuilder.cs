using System.Globalization;
using System.Text.RegularExpressions;
using KoeNote.App.Models;

namespace KoeNote.App.Services.Transcript;

public static class ReadableDocumentBlockBuilder
{
    private static readonly Regex TimestampedBlockPattern = new(
        @"^\s*\[?(?<start>\d{1,2}:\d{2}(?::\d{2})?)\s*(?:(?:-|--|~|\u301c|\uff5e|\u2013|\u2014)\s*(?<end>\d{1,2}:\d{2}(?::\d{2})?))?\]?\s*(?:(?<speaker>[^:：\r\n]{1,48})[:：]\s*)?(?<text>.*)$",
        RegexOptions.Compiled);

    public static IReadOnlyList<ReadableDocumentBlock> Build(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var blocks = new List<ReadableDocumentBlock>();
        var current = new List<string>();
        var sourceLineIndex = 0;

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                AddBlock(blocks, current, sourceLineIndex);
                current.Clear();
                sourceLineIndex = index + 1;
                continue;
            }

            if (current.Count > 0 && LooksLikeTimestampedBlock(line))
            {
                AddBlock(blocks, current, sourceLineIndex);
                current.Clear();
                sourceLineIndex = index;
            }

            if (current.Count == 0)
            {
                sourceLineIndex = index;
            }

            current.Add(line.TrimEnd());
        }

        AddBlock(blocks, current, sourceLineIndex);
        return blocks;
    }

    private static void AddBlock(ICollection<ReadableDocumentBlock> blocks, IReadOnlyList<string> lines, int sourceLineIndex)
    {
        var trimmedLines = lines
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
        if (trimmedLines.Length == 0)
        {
            return;
        }

        var firstLine = trimmedLines[0];
        var match = TimestampedBlockPattern.Match(firstLine);
        if (match.Success)
        {
            var textLines = new List<string>(trimmedLines.Length)
            {
                match.Groups["text"].Value.Trim()
            };
            textLines.AddRange(trimmedLines.Skip(1));
            var text = string.Join(Environment.NewLine, textLines.Where(static line => line.Length > 0)).Trim();
            if (text.Length > 0)
            {
                var startText = match.Groups["start"].Value;
                var endText = match.Groups["end"].Value;
                blocks.Add(new ReadableDocumentBlock(
                    match.Groups["speaker"].Value.Trim(),
                    FormatTimeRange(startText, endText),
                    text,
                    sourceLineIndex,
                    TryParseTimestamp(startText, out var startSeconds) ? startSeconds : null,
                    TryParseTimestamp(endText, out var endSeconds) ? endSeconds : null));
                return;
            }
        }

        blocks.Add(new ReadableDocumentBlock(
            string.Empty,
            string.Empty,
            string.Join(Environment.NewLine, trimmedLines).Trim(),
            sourceLineIndex,
            null,
            null));
    }

    private static bool LooksLikeTimestampedBlock(string line)
    {
        return TimestampedBlockPattern.IsMatch(line);
    }

    private static string FormatTimeRange(string startText, string endText)
    {
        if (string.IsNullOrWhiteSpace(startText))
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(endText)
            ? startText
            : $"{startText} - {endText}";
    }

    private static bool TryParseTimestamp(string value, out double seconds)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            seconds = 0;
            return false;
        }

        var parts = value.Split(':');
        if (parts.Length == 2 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var secondsPart))
        {
            seconds = minutes * 60 + secondsPart;
            return true;
        }

        if (parts.Length == 3 &&
            int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var hours) &&
            int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes) &&
            int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out secondsPart))
        {
            seconds = hours * 3600 + minutes * 60 + secondsPart;
            return true;
        }

        seconds = 0;
        return false;
    }
}
