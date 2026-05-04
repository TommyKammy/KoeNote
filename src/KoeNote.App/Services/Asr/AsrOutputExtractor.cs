using System.Text.Json;

namespace KoeNote.App.Services.Asr;

public static class AsrOutputExtractor
{
    public static string ExtractJson(string standardOutput, string standardError)
    {
        var outputJson = TryExtractJson(standardOutput);
        if (outputJson is not null)
        {
            return outputJson;
        }

        var errorJson = TryExtractJson(standardError);
        if (errorJson is not null)
        {
            return errorJson;
        }

        return standardOutput;
    }

    private static string? TryExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();
        if (LooksLikeJson(trimmed) && IsValidJson(trimmed) && LooksLikeDomainJson(trimmed))
        {
            return trimmed;
        }

        foreach (var start in FindJsonStarts(text))
        {
            var end = FindMatchingJsonEnd(text, start);
            if (end < start)
            {
                continue;
            }

            var candidate = text[start..(end + 1)];
            if (IsValidJson(candidate) && LooksLikeDomainJson(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool LooksLikeJson(string text)
    {
        return (text.StartsWith('{') && text.EndsWith('}'))
            || (text.StartsWith('[') && text.EndsWith(']'));
    }

    private static IEnumerable<int> FindJsonStarts(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '{' or '[')
            {
                yield return i;
            }
        }
    }

    private static int FindMatchingJsonEnd(string text, int start)
    {
        var open = text[start];
        var close = open == '{' ? '}' : ']';
        var depth = 0;
        var inString = false;
        var escaping = false;

        for (var i = start; i < text.Length; i++)
        {
            var character = text[i];

            if (escaping)
            {
                escaping = false;
                continue;
            }

            if (character == '\\' && inString)
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (character == open)
            {
                depth++;
            }
            else if (character == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool IsValidJson(string candidate)
    {
        try
        {
            using var _ = JsonDocument.Parse(candidate);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool LooksLikeDomainJson(string candidate)
    {
        try
        {
            using var document = JsonDocument.Parse(candidate);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => true,
                JsonValueKind.Array => document.RootElement.EnumerateArray().Any(static item => item.ValueKind == JsonValueKind.Object),
                _ => false
            };
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
