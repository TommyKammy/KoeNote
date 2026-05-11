namespace KoeNote.App.Services.Transcript;

public static class TranscriptPolishingOutputNormalizer
{
    public static string Normalize(string content)
    {
        var markerContent = ExtractMarkedBlocks(content);
        var normalized = ExtractOutputSection(markerContent ?? content)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized
            .Split('\n')
            .Select(static line => line.TrimEnd())
            .ToArray();
        var builder = new List<string>(lines.Length);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (ShouldDropGeneratedWrapperLine(trimmed))
            {
                continue;
            }

            if (IsTranscriptBlockLine(trimmed) && HasPreviousContentLine(builder))
            {
                AddBlankLineIfNeeded(builder);
            }

            if (IsConsecutiveDuplicateContentLine(builder, line))
            {
                continue;
            }

            builder.Add(line);
        }

        return string.Join(Environment.NewLine, TrimBlankEdges(builder)).Trim();
    }

    public static bool IsUsableDocument(string content, out string reason)
    {
        var normalized = Normalize(content);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            reason = "empty";
            return false;
        }

        if (normalized.Contains('\uFFFD', StringComparison.Ordinal))
        {
            reason = "contains replacement characters";
            return false;
        }

        if (!HasTranscriptBlockLine(normalized))
        {
            reason = "missing timestamped speaker block";
            return false;
        }

        var repeatedLine = normalized
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length >= 8)
            .GroupBy(static line => line, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() >= 4);
        if (repeatedLine is not null)
        {
            reason = $"repeated line: {repeatedLine.Key}";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string? ExtractMarkedBlocks(string content)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        var blocks = new List<string>();
        var current = new List<string>();
        var insideBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (IsBeginBlockLine(trimmed))
            {
                if (insideBlock && current.Count > 0)
                {
                    blocks.Add(string.Join("\n", TrimBlankEdges(current)));
                }

                current.Clear();
                insideBlock = true;
                continue;
            }

            if (IsEndBlockLine(trimmed))
            {
                if (insideBlock)
                {
                    blocks.Add(string.Join("\n", TrimBlankEdges(current)));
                    current.Clear();
                    insideBlock = false;
                }

                continue;
            }

            if (insideBlock)
            {
                current.Add(line);
            }
        }

        if (insideBlock && current.Count > 0)
        {
            blocks.Add(string.Join("\n", TrimBlankEdges(current)));
        }

        var nonEmptyBlocks = blocks
            .Select(static block => block.Trim())
            .Where(static block => block.Length > 0)
            .ToArray();
        return nonEmptyBlocks.Length == 0 ? null : string.Join("\n\n", nonEmptyBlocks);
    }

    private static string ExtractOutputSection(string content)
    {
        const string marker = "Output:";
        var index = content.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? content : content[(index + marker.Length)..];
    }

    private static bool ShouldDropGeneratedWrapperLine(string trimmedLine)
    {
        return trimmedLine.Length == 0 ||
            trimmedLine.StartsWith("#", StringComparison.Ordinal) ||
            IsBeginBlockLine(trimmedLine) ||
            IsEndBlockLine(trimmedLine) ||
            IsStandalonePunctuation(trimmedLine);
    }

    private static bool IsBeginBlockLine(string trimmedLine)
    {
        return string.Equals(trimmedLine, "BEGIN_BLOCK", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("BEGIN_BLOCK ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEndBlockLine(string trimmedLine)
    {
        return string.Equals(trimmedLine, "END_BLOCK", StringComparison.OrdinalIgnoreCase) ||
            trimmedLine.StartsWith("END_BLOCK ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConsecutiveDuplicateContentLine(IReadOnlyList<string> lines, string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        for (var index = lines.Count - 1; index >= 0; index--)
        {
            var previous = lines[index].Trim();
            if (previous.Length == 0)
            {
                continue;
            }

            return string.Equals(previous, trimmed, StringComparison.Ordinal);
        }

        return false;
    }

    private static bool IsStandalonePunctuation(string value)
    {
        return value.All(static character =>
            character is '。' or '、' or '.' or ',' or ':' or '：' or ';' or '；' or '!' or '！' or '?' or '？');
    }

    private static bool IsTranscriptBlockLine(string trimmedLine)
    {
        if (trimmedLine.Length == 0)
        {
            return false;
        }

        var offset = trimmedLine[0] == '[' ? 1 : 0;
        return trimmedLine.Length > offset + 4 &&
            char.IsDigit(trimmedLine[offset]) &&
            char.IsDigit(trimmedLine[offset + 1]) &&
            trimmedLine[offset + 2] == ':' &&
            char.IsDigit(trimmedLine[offset + 3]) &&
            char.IsDigit(trimmedLine[offset + 4]);
    }

    private static bool HasTranscriptBlockLine(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Any(static line => IsTranscriptBlockLine(line.TrimStart()));
    }

    private static bool HasPreviousContentLine(IReadOnlyList<string> lines)
    {
        return lines.Any(static line => !string.IsNullOrWhiteSpace(line));
    }

    private static void AddBlankLineIfNeeded(IList<string> lines)
    {
        if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
        {
            lines.Add(string.Empty);
        }
    }

    private static IReadOnlyList<string> TrimBlankEdges(IReadOnlyList<string> lines)
    {
        var start = 0;
        while (start < lines.Count && string.IsNullOrWhiteSpace(lines[start]))
        {
            start++;
        }

        var end = lines.Count - 1;
        while (end >= start && string.IsNullOrWhiteSpace(lines[end]))
        {
            end--;
        }

        return start > end ? [] : lines.Skip(start).Take(end - start + 1).ToArray();
    }
}
