using System.Text.RegularExpressions;

namespace KoeNote.App.Services.Transcript;

public static class ReadableDocumentSpeakerRenamer
{
    public const int MaxSpeakerNameLength = 80;

    private const string TimestampPattern = @"\d{1,2}:\d{2}(?::\d{2})?";
    private const string RangeSeparatorPattern = @"(?:--|-|~|\u301c|\uFF5E|\u2013|\u2014|\u2212|\uFF0D|\u30FC)";
    private const string SpeakerPattern = @"[^:\uFF1A\r\n]{1,80}";

    private static readonly Regex TimestampedSpeakerLinePattern = new(
        @"^(?<prefix>\s*\[" + TimestampPattern + @"\s*(?:" + RangeSeparatorPattern + @"\s*" + TimestampPattern + @")?\]\s*)(?<speaker>" + SpeakerPattern + @")(?<separator>[:\uFF1A]\s*)(?<text>.*)$",
        RegexOptions.Compiled);

    public static ReadableDocumentSpeakerRenameResult Rename(string content, string currentSpeaker, string replacementSpeaker)
    {
        if (string.IsNullOrWhiteSpace(content) ||
            string.IsNullOrWhiteSpace(currentSpeaker) ||
            string.IsNullOrWhiteSpace(replacementSpeaker))
        {
            return new ReadableDocumentSpeakerRenameResult(content, 0);
        }

        var normalizedCurrentSpeaker = currentSpeaker.Trim();
        var normalizedReplacementSpeaker = replacementSpeaker.Trim();
        if (!IsValidSpeakerName(normalizedReplacementSpeaker))
        {
            return new ReadableDocumentSpeakerRenameResult(content, 0);
        }

        if (string.Equals(normalizedCurrentSpeaker, normalizedReplacementSpeaker, StringComparison.Ordinal))
        {
            return new ReadableDocumentSpeakerRenameResult(content, 0);
        }

        var newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var updatedBlockCount = 0;

        foreach (var block in ReadableDocumentBlockBuilder.Build(content))
        {
            if (!string.Equals(block.Speaker, normalizedCurrentSpeaker, StringComparison.Ordinal) ||
                block.SourceLineIndex < 0 ||
                block.SourceLineIndex >= lines.Length)
            {
                continue;
            }

            var match = TimestampedSpeakerLinePattern.Match(lines[block.SourceLineIndex]);
            if (!match.Success ||
                !string.Equals(match.Groups["speaker"].Value.Trim(), normalizedCurrentSpeaker, StringComparison.Ordinal))
            {
                continue;
            }

            lines[block.SourceLineIndex] =
                match.Groups["prefix"].Value +
                normalizedReplacementSpeaker +
                match.Groups["separator"].Value +
                match.Groups["text"].Value;
            updatedBlockCount++;
        }

        return new ReadableDocumentSpeakerRenameResult(
            updatedBlockCount > 0 ? string.Join(newline, lines) : content,
            updatedBlockCount);
    }

    public static bool IsValidSpeakerName(string speakerName)
    {
        if (string.IsNullOrWhiteSpace(speakerName))
        {
            return false;
        }

        var normalized = speakerName.Trim();
        return normalized.Length <= MaxSpeakerNameLength &&
            !normalized.Contains(':', StringComparison.Ordinal) &&
            !normalized.Contains('：', StringComparison.Ordinal) &&
            !normalized.Any(static character => char.IsControl(character));
    }
}

public sealed record ReadableDocumentSpeakerRenameResult(string Content, int UpdatedBlockCount)
{
    public bool Changed => UpdatedBlockCount > 0;
}
