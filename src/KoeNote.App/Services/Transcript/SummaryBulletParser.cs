using System.Text;

namespace KoeNote.App.Services.Transcript;

internal static class SummaryBulletParser
{
    public static string[] BuildSegmentFallbackBullets(
        IReadOnlyList<TranscriptReadModel> segments,
        int limit)
    {
        return segments
            .Where(static segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(static segment => SummaryTextNormalizer.TrimForSummary(segment.Text, 140))
            .Take(limit)
            .ToArray();
    }

    public static void AppendBullets(StringBuilder builder, IEnumerable<string> bullets)
    {
        foreach (var bullet in DeduplicateBullets(bullets))
        {
            builder
                .Append("- ")
                .AppendLine(bullet);
        }
    }

    public static string[] ExtractSectionBullets(
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        IReadOnlyCollection<string> sectionNames,
        int limit)
    {
        var bullets = new List<string>();
        foreach (var chunk in chunkResults)
        {
            var inSection = false;
            foreach (var rawLine in chunk.Content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var heading = line[3..].Trim();
                    inSection = sectionNames.Any(sectionName => heading.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
                    continue;
                }

                if (!inSection || (!line.StartsWith("- ", StringComparison.Ordinal) && !line.StartsWith("* ", StringComparison.Ordinal)))
                {
                    continue;
                }

                var bullet = line[2..].Trim();
                if (bullet.Length == 0 || bullet.Equals("Unspecified.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bullets.Add(SummaryTextNormalizer.TrimForSummary(bullet, 220));
                if (bullets.Count >= limit)
                {
                    return bullets.ToArray();
                }
            }
        }

        return bullets.ToArray();
    }

    private static IEnumerable<string> DeduplicateBullets(IEnumerable<string> bullets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bullet in bullets)
        {
            var normalized = SummaryTextNormalizer.TrimForSummary(SummaryTextNormalizer.StripSourceReferences(bullet), 220).Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var key = normalized.Replace("\u3002", string.Empty, StringComparison.Ordinal);
            if (seen.Add(key))
            {
                yield return normalized;
            }
        }
    }
}
