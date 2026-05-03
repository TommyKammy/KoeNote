using KoeNote.App.Models;

namespace KoeNote.App.Services.Review;

public sealed class TextDiffService
{
    public IReadOnlyList<DiffToken> BuildInlineDiff(string original, string suggested)
    {
        original ??= string.Empty;
        suggested ??= string.Empty;

        if (string.Equals(original, suggested, StringComparison.Ordinal))
        {
            return string.IsNullOrEmpty(original) ? [] : [new DiffToken(original, DiffKind.Equal)];
        }

        var originalChars = original.ToCharArray();
        var suggestedChars = suggested.ToCharArray();
        var lengths = BuildLcsLengths(originalChars, suggestedChars);
        var rawTokens = new List<DiffToken>();
        Walk(originalChars, suggestedChars, lengths, 0, 0, rawTokens);

        return MergeReplacementPairs(Compact(rawTokens));
    }

    private static int[,] BuildLcsLengths(char[] original, char[] suggested)
    {
        var lengths = new int[original.Length + 1, suggested.Length + 1];
        for (var i = original.Length - 1; i >= 0; i--)
        {
            for (var j = suggested.Length - 1; j >= 0; j--)
            {
                lengths[i, j] = original[i] == suggested[j]
                    ? lengths[i + 1, j + 1] + 1
                    : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
            }
        }

        return lengths;
    }

    private static void Walk(char[] original, char[] suggested, int[,] lengths, int i, int j, List<DiffToken> tokens)
    {
        while (i < original.Length || j < suggested.Length)
        {
            if (i < original.Length && j < suggested.Length && original[i] == suggested[j])
            {
                tokens.Add(new DiffToken(original[i].ToString(), DiffKind.Equal));
                i++;
                j++;
                continue;
            }

            if (j < suggested.Length && (i == original.Length || lengths[i, j + 1] >= lengths[i + 1, j]))
            {
                tokens.Add(new DiffToken(suggested[j].ToString(), DiffKind.Inserted));
                j++;
                continue;
            }

            if (i < original.Length)
            {
                tokens.Add(new DiffToken(original[i].ToString(), DiffKind.Deleted));
                i++;
            }
        }
    }

    private static IReadOnlyList<DiffToken> Compact(IReadOnlyList<DiffToken> tokens)
    {
        var compacted = new List<DiffToken>();
        foreach (var token in tokens)
        {
            if (compacted.LastOrDefault() is { } last && last.Kind == token.Kind)
            {
                compacted[^1] = last with { Text = last.Text + token.Text };
                continue;
            }

            compacted.Add(token);
        }

        return compacted;
    }

    private static IReadOnlyList<DiffToken> MergeReplacementPairs(IReadOnlyList<DiffToken> tokens)
    {
        var merged = new List<DiffToken>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (i + 1 < tokens.Count
                && tokens[i].Kind == DiffKind.Deleted
                && tokens[i + 1].Kind == DiffKind.Inserted)
            {
                merged.Add(new DiffToken($"{tokens[i].Text} -> {tokens[i + 1].Text}", DiffKind.Replaced));
                i++;
                continue;
            }

            if (i + 1 < tokens.Count
                && tokens[i].Kind == DiffKind.Inserted
                && tokens[i + 1].Kind == DiffKind.Deleted)
            {
                merged.Add(new DiffToken($"{tokens[i + 1].Text} -> {tokens[i].Text}", DiffKind.Replaced));
                i++;
                continue;
            }

            merged.Add(tokens[i]);
        }

        return merged;
    }
}
