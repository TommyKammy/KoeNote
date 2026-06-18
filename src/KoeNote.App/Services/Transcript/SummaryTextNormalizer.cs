using System.Text.RegularExpressions;

namespace KoeNote.App.Services.Transcript;

internal static partial class SummaryTextNormalizer
{
    public static string NormalizeUserFacingSummary(string content)
    {
        var normalizedLines = new List<string>();
        var currentSection = string.Empty;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                currentSection = trimmed[3..].Trim();
                normalizedLines.Add(line);
                continue;
            }

            if (trimmed.Length == 0)
            {
                normalizedLines.Add(line);
                continue;
            }

            if (IsUnspecifiedSection(currentSection) && IsUnspecifiedLine(trimmed))
            {
                normalizedLines.Add("- Unspecified.");
                continue;
            }

            if (IsKeywordSection(currentSection))
            {
                AppendKeywordLines(normalizedLines, trimmed);
                continue;
            }

            normalizedLines.Add(NormalizeUserFacingSummaryLine(line));
        }

        return string.Join(Environment.NewLine, normalizedLines).Trim();
    }

    public static string TrimForSummary(string text, int maxLength)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
    }

    public static string StripSourceReferences(string text)
    {
        var withoutSegmentRefs = SourceReferenceRegex().Replace(text ?? string.Empty, string.Empty);
        var withoutEmphasis = BoldMarkdownRegex().Replace(withoutSegmentRefs, "$1");
        return string.Join(" ", withoutEmphasis.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    private static string NormalizeUserFacingSummaryLine(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("- ", StringComparison.Ordinal) &&
            !trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return line;
        }

        var indentLength = line.Length - trimmed.Length;
        var prefix = line[..indentLength];
        return prefix + trimmed[..2] + StripSourceReferences(trimmed[2..]);
    }

    private static bool IsUnspecifiedSection(string section)
    {
        return section.Equals("Decisions", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("Action items", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("Open questions", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKeywordSection(string section)
    {
        return section.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("キーワード", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnspecifiedLine(string line)
    {
        return line.Trim().TrimEnd('.').Equals("Unspecified", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendKeywordLines(List<string> lines, string line)
    {
        var keywordSource = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)
            ? line[2..]
            : line;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in SummaryKeywordExtractor.SplitKeywords(keywordSource))
        {
            var normalized = StripSourceReferences(keyword).Trim().TrimEnd('\u3002', '.', ',');
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            lines.Add("- " + normalized);
            if (seen.Count >= 12)
            {
                return;
            }
        }
    }

    [GeneratedRegex(@"\s*(?:[\(\uFF08]\s*\[?\s*(?:segment_id:\s*)?\d+(?:\s*(?:,|-|\u2011|\u2013|\u2014|\u3001|\uFF5E|~)\s*\d+)*\s*\]?\s*[\)\uFF09]|\u3010\s*\d+(?:\s*(?:,|-|\u2011|\u2013|\u2014|\u3001|\uFF5E|~)\s*\d+)*\s*\u3011|\[\s*\d+(?:\s*(?:,|-|\u2011|\u2013|\u2014|\u3001|\uFF5E|~)\s*\d+)*\s*\])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceReferenceRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldMarkdownRegex();
}
