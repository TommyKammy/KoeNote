using System.Text.RegularExpressions;

namespace KoeNote.App.Services.Transcript;

internal static partial class SummaryKeywordExtractor
{
    public static string[] BuildFallbackKeywords(
        IReadOnlyList<string> overviews,
        IReadOnlyList<string> keyPoints,
        IReadOnlyList<string> segmentFallbackBullets)
    {
        var sourceSentences = overviews
            .Concat(keyPoints)
            .Concat(segmentFallbackBullets)
            .Select(static sentence => NormalizeKeywordComparisonKey(sentence))
            .Where(static sentence => sentence.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return overviews
            .Concat(keyPoints)
            .Concat(segmentFallbackBullets)
            .SelectMany(SplitKeywordCandidates)
            .Select(NormalizeKeywordCandidate)
            .Where(keyword => IsUsefulKeyword(keyword, sourceSentences))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .DefaultIfEmpty("summary")
            .ToArray();
    }

    public static string[] NormalizeKeywordBullets(
        IReadOnlyList<string> keywordBullets,
        IEnumerable<string> sourceSentences)
    {
        var sourceSentenceKeys = sourceSentences
            .Select(static sentence => NormalizeKeywordComparisonKey(sentence))
            .Where(static sentence => sentence.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return keywordBullets
            .SelectMany(SplitKeywords)
            .Select(static keyword => SummaryTextNormalizer.StripSourceReferences(keyword).Trim().TrimEnd('\u3002', '.', ','))
            .Where(keyword => IsUsefulKeyword(keyword, sourceSentenceKeys))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
    }

    public static IEnumerable<string> SplitKeywords(string text)
    {
        return (text ?? string.Empty)
            .Split([',', '\u3001'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> SplitKeywordCandidates(string text)
    {
        return (text ?? string.Empty)
            .Replace(":", " ", StringComparison.Ordinal)
            .Replace("\uFF1A", " ", StringComparison.Ordinal)
            .Split([' ', '\t', ',', '.', '\u3001', '\u3002', '\u30fb', '/', '\u3000'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitJapaneseKeywordToken)
            .Where(static token => !token.Equals("Unspecified", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SplitJapaneseKeywordToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            yield break;
        }

        yield return token;

        foreach (var part in JapaneseKeywordParticleRegex()
            .Split(token)
            .Select(NormalizeKeywordCandidate)
            .Where(static part => part.Length >= 2))
        {
            yield return part;
        }
    }

    private static string NormalizeKeywordCandidate(string keyword)
    {
        var normalized = SummaryTextNormalizer.StripSourceReferences(keyword).Trim().TrimEnd('\u3002', '.', ',');
        foreach (var prefix in new[] { "この", "その", "あの" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length + 1)
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized;
    }

    private static bool IsUsefulKeyword(string keyword, IReadOnlySet<string> sourceSentenceKeys)
    {
        var normalized = NormalizeKeywordCandidate(keyword);
        if (normalized.Length < 2)
        {
            return false;
        }

        if (sourceSentenceKeys.Contains(NormalizeKeywordComparisonKey(normalized)))
        {
            return false;
        }

        return normalized.Length <= 28 &&
            CountJapaneseParticles(normalized) <= 1 &&
            !normalized.Contains("です", StringComparison.Ordinal) &&
            !normalized.Contains("ます", StringComparison.Ordinal);
    }

    private static string NormalizeKeywordComparisonKey(string text)
    {
        return SummaryTextNormalizer.StripSourceReferences(text)
            .Trim()
            .TrimStart('-', '*')
            .Trim()
            .TrimEnd('\u3002', '.', ',');
    }

    private static int CountJapaneseParticles(string text)
    {
        string[] particles = ["は", "が", "を", "に", "で", "と", "も", "へ", "から", "まで", "より"];
        return particles.Count(particle => text.Contains(particle, StringComparison.Ordinal));
    }

    [GeneratedRegex("(?:には|では|から|まで|より|という|って|は|が|を|に|で|と|も|へ|の)", RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseKeywordParticleRegex();
}
