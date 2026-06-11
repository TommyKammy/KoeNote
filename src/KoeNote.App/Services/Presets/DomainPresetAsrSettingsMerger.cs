namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetAsrSettingsMerger
{
    private const int MaxAsrHotwordLength = 24;

    public string MergeContext(string currentContext, string? presetContext)
    {
        var current = currentContext.Trim();
        var preset = (presetContext ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(preset))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return preset;
        }

        if (current.Contains(preset, StringComparison.Ordinal))
        {
            return current;
        }

        return string.Join(Environment.NewLine + Environment.NewLine, current, preset);
    }

    public string RemoveContextBlock(string currentContext, string? presetContext)
    {
        var preset = (presetContext ?? string.Empty).Trim();
        if (preset.Length == 0)
        {
            return currentContext.Trim();
        }

        var blocks = currentContext
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(block => !string.Equals(block, preset, StringComparison.Ordinal))
            .ToArray();
        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    public DomainPresetHotwordMergeResult MergeHotwords(
        IReadOnlyList<string> currentHotwords,
        IReadOnlyList<string> presetHotwords)
    {
        var hotwords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hotword in currentHotwords)
        {
            var normalized = NormalizeHotword(hotword);
            if (normalized.Length > 0 && seen.Add(normalized))
            {
                hotwords.Add(normalized);
            }
        }

        var added = 0;
        var skipped = 0;
        foreach (var hotword in presetHotwords)
        {
            var normalized = NormalizeHotword(hotword);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (seen.Add(normalized))
            {
                hotwords.Add(normalized);
                added++;
            }
            else
            {
                skipped++;
            }
        }

        return new DomainPresetHotwordMergeResult(hotwords, added, skipped);
    }

    public IReadOnlyList<string> RemoveHotwords(
        IReadOnlyList<string> currentHotwords,
        IReadOnlyList<string> presetHotwords,
        out int removedCount)
    {
        var presetSet = presetHotwords
            .Select(NormalizeHotword)
            .Where(static hotword => hotword.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var next = new List<string>();
        removedCount = 0;
        foreach (var hotword in currentHotwords)
        {
            var normalized = NormalizeHotword(hotword);
            if (normalized.Length == 0)
            {
                continue;
            }

            if (presetSet.Contains(normalized))
            {
                removedCount++;
                continue;
            }

            next.Add(normalized);
        }

        return next;
    }

    public bool ShouldRemovePresetHotwords(int addedHotwordCount, IReadOnlyList<string> presetHotwords)
    {
        var presetHotwordCount = presetHotwords
            .Select(NormalizeHotword)
            .Where(static hotword => hotword.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return presetHotwordCount > 0 && addedHotwordCount == presetHotwordCount;
    }

    public bool IsAsrHotwordCandidate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxAsrHotwordLength)
        {
            return false;
        }

        return !trimmed.Any(static character =>
            char.IsWhiteSpace(character) ||
            char.IsPunctuation(character) ||
            char.IsSymbol(character));
    }

    private static string NormalizeHotword(string hotword)
    {
        return hotword.Trim();
    }
}

internal sealed record DomainPresetHotwordMergeResult(IReadOnlyList<string> Hotwords, int AddedCount, int SkippedCount);
