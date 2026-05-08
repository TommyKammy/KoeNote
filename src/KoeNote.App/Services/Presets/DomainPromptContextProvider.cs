using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Presets;

public sealed record DomainPromptContext(
    IReadOnlyList<DomainPromptTerm> Terms,
    IReadOnlyList<DomainPromptCorrectionPair> CorrectionPairs,
    IReadOnlyList<string> ReviewGuidelines)
{
    public bool IsEmpty => Terms.Count == 0 && CorrectionPairs.Count == 0 && ReviewGuidelines.Count == 0;
}

public sealed record DomainPromptTerm(string Surface, string Category);

public sealed record DomainPromptCorrectionPair(
    string WrongText,
    string CorrectText,
    string IssueType,
    string Scope);

public sealed record DomainPromptContextLimits(
    int TermLimit = 30,
    int CorrectionPairLimit = 20,
    int ReviewGuidelineLimit = 10)
{
    public static DomainPromptContextLimits SummaryDefault { get; } = new();

    public static DomainPromptContextLimits BonsaiSummary { get; } = new(
        TermLimit: 10,
        CorrectionPairLimit: 10,
        ReviewGuidelineLimit: 5);
}

public sealed class DomainPromptContextProvider(AppPaths paths)
{
    public DomainPromptContext LoadForSummary(DomainPromptContextLimits? limits = null)
    {
        return LoadForSummary(null, limits);
    }

    public DomainPromptContext LoadForSummary(string? sourceText, DomainPromptContextLimits? limits = null)
    {
        limits ??= DomainPromptContextLimits.SummaryDefault;
        var matchSource = NormalizeForMatch(sourceText);

        using var connection = SqliteConnectionFactory.Open(paths);
        return new DomainPromptContext(
            LoadTerms(connection, limits.TermLimit, matchSource),
            LoadCorrectionPairs(connection, limits.CorrectionPairLimit, matchSource),
            LoadReviewGuidelines(connection, limits.ReviewGuidelineLimit));
    }

    private static IReadOnlyList<DomainPromptTerm> LoadTerms(SqliteConnection connection, int limit, string matchSource)
    {
        if (limit <= 0)
        {
            return [];
        }

        var terms = LoadAsrHotwords(connection)
            .Select(static hotword => new DomainPromptTerm(hotword, "asr_hotword"))
            .ToList();

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT surface, category
            FROM user_terms
            WHERE enabled = 1
                AND trim(surface) <> ''
            ORDER BY updated_at DESC, surface
            LIMIT $candidate_limit;
            """;
        command.Parameters.AddWithValue("$candidate_limit", ResolveCandidateLimit(limit, matchSource));

        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                terms.Add(new DomainPromptTerm(
                    reader.GetString(0),
                    reader.GetString(1)));
            }
        }

        var distinctTerms = terms
            .GroupBy(static term => term.Surface, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToArray();

        return SelectRelevant(
            distinctTerms,
            limit,
            matchSource,
            static term => [term.Surface]);
    }

    private static IReadOnlyList<string> LoadAsrHotwords(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT hotwords_text
            FROM asr_settings
            WHERE settings_id = 1;
            """;

        var hotwordsText = command.ExecuteScalar() as string;
        if (string.IsNullOrWhiteSpace(hotwordsText))
        {
            return [];
        }

        return hotwordsText
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DomainPromptCorrectionPair> LoadCorrectionPairs(
        SqliteConnection connection,
        int limit,
        string matchSource)
    {
        if (limit <= 0)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT wrong_text, correct_text, issue_type, scope
            FROM correction_memory
            WHERE enabled = 1
                AND trim(wrong_text) <> ''
                AND trim(correct_text) <> ''
                AND wrong_text <> correct_text
            ORDER BY accepted_count DESC, updated_at DESC, wrong_text
            LIMIT $candidate_limit;
            """;
        command.Parameters.AddWithValue("$candidate_limit", ResolveCandidateLimit(limit, matchSource));

        using var reader = command.ExecuteReader();
        var pairs = new List<DomainPromptCorrectionPair>();
        while (reader.Read())
        {
            pairs.Add(new DomainPromptCorrectionPair(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return SelectRelevant(
            pairs,
            limit,
            matchSource,
            static pair => [pair.WrongText, pair.CorrectText]);
    }

    private static IReadOnlyList<string> LoadReviewGuidelines(SqliteConnection connection, int limit)
    {
        if (limit <= 0)
        {
            return [];
        }

        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT guideline_text, MAX(updated_at) AS latest_updated_at
            FROM review_guidelines
            WHERE enabled = 1
                AND trim(guideline_text) <> ''
            GROUP BY guideline_text
            ORDER BY latest_updated_at DESC, guideline_text
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var guidelines = new List<string>();
        while (reader.Read())
        {
            guidelines.Add(reader.GetString(0));
        }

        return guidelines;
    }

    private static IReadOnlyList<T> SelectRelevant<T>(
        IReadOnlyList<T> candidates,
        int limit,
        string matchSource,
        Func<T, IReadOnlyList<string>> getMatchTexts)
    {
        if (candidates.Count == 0 || limit <= 0)
        {
            return [];
        }

        if (string.IsNullOrWhiteSpace(matchSource))
        {
            return candidates.Take(limit).ToArray();
        }

        var matched = candidates
            .Where(candidate => getMatchTexts(candidate).Any(text => ContainsNormalized(matchSource, text)))
            .Take(limit)
            .ToArray();

        return matched.Length > 0
            ? matched
            : candidates.Take(limit).ToArray();
    }

    private static int ResolveCandidateLimit(int limit, string matchSource)
    {
        return string.IsNullOrWhiteSpace(matchSource)
            ? limit
            : Math.Min(Math.Max(limit * 8, limit + 50), 200);
    }

    private static bool ContainsNormalized(string normalizedSource, string value)
    {
        var normalizedValue = NormalizeForMatch(value);
        return normalizedValue.Length > 0 && normalizedSource.Contains(normalizedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeForMatch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length);
        foreach (var character in normalized)
        {
            if (ShouldIgnoreForMatch(character))
            {
                continue;
            }

            builder.Append(NormalizeNumericCharacter(character));
        }

        return builder.ToString();
    }

    private static bool ShouldIgnoreForMatch(char character)
    {
        if (char.IsWhiteSpace(character))
        {
            return true;
        }

        var category = CharUnicodeInfo.GetUnicodeCategory(character);
        return category is UnicodeCategory.Control
            or UnicodeCategory.DashPunctuation
            or UnicodeCategory.OpenPunctuation
            or UnicodeCategory.ClosePunctuation
            or UnicodeCategory.InitialQuotePunctuation
            or UnicodeCategory.FinalQuotePunctuation
            or UnicodeCategory.OtherPunctuation;
    }

    private static char NormalizeNumericCharacter(char character)
    {
        return character switch
        {
            '〇' or '零' => '0',
            '一' => '1',
            '二' => '2',
            '三' => '3',
            '四' => '4',
            '五' => '5',
            '六' => '6',
            '七' => '7',
            '八' => '8',
            '九' => '9',
            _ => character
        };
    }
}
