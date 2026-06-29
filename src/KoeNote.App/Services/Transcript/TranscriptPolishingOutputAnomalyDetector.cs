using System.Text.RegularExpressions;

namespace KoeNote.App.Services.Transcript;

internal static class TranscriptPolishingOutputAnomalyDetector
{
    private static readonly Regex RepeatedZeroRegex = new(
        @"0{24,}",
        RegexOptions.CultureInvariant);

    public static bool TryFindCriticalAnomaly(string rawContent, string normalizedContent, out string reason)
    {
        var combined = $"{rawContent}\n{normalizedContent}";
        if (RepeatedZeroRegex.IsMatch(combined))
        {
            reason = "repeated zero token run";
            return true;
        }

        if (combined.Contains("<|channel>", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("<|channel|>", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("<channel|>", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("<|channel>thought", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("reasoning_content", StringComparison.OrdinalIgnoreCase))
        {
            reason = "visible reasoning channel token";
            return true;
        }

        if (combined.Contains("<think", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("</think>", StringComparison.OrdinalIgnoreCase))
        {
            reason = "visible thinking block";
            return true;
        }

        if (HasUnbalancedBlockMarkers(rawContent, out reason))
        {
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool HasUnbalancedBlockMarkers(string content, out string reason)
    {
        var beginCount = CountMarkerLines(content, "BEGIN_BLOCK");
        var endCount = CountMarkerLines(content, "END_BLOCK");
        if (beginCount == 0 && endCount == 0)
        {
            reason = string.Empty;
            return false;
        }

        if (beginCount != endCount)
        {
            reason = $"unbalanced block markers: BEGIN_BLOCK={beginCount}, END_BLOCK={endCount}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static int CountMarkerLines(string content, string marker)
    {
        return (content ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Count(line => line.TrimStart().StartsWith(marker, StringComparison.OrdinalIgnoreCase));
    }
}
