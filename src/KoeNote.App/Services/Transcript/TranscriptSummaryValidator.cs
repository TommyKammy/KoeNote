namespace KoeNote.App.Services.Transcript;

public sealed record TranscriptSummaryValidationResult(bool IsValid, string Reason)
{
    public static TranscriptSummaryValidationResult Valid { get; } = new(true, string.Empty);
}

public static class TranscriptSummaryValidator
{
    public static TranscriptSummaryValidationResult Validate(
        string content,
        string validationMode,
        bool requireStructuredSections)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new TranscriptSummaryValidationResult(false, "Transcript summary returned empty output.");
        }

        var normalized = content.Trim();
        if (ContainsUnsupportedOutput(normalized))
        {
            return new TranscriptSummaryValidationResult(false, "Transcript summary output contained unsupported assistant chatter or prompt echo.");
        }

        if (LooksLikeInstructionEcho(normalized))
        {
            return new TranscriptSummaryValidationResult(false, "Transcript summary output looked like an instruction echo instead of a summary.");
        }

        if (!requireStructuredSections)
        {
            return TranscriptSummaryValidationResult.Valid;
        }

        var headingCount = CountMarkdownHeadings(normalized);
        if (headingCount < 2)
        {
            return new TranscriptSummaryValidationResult(false, "Transcript summary output did not contain enough Markdown sections.");
        }

        if (HasRepeatedMarkdownHeading(normalized))
        {
            return new TranscriptSummaryValidationResult(false, "Transcript summary output repeated the same Markdown section heading.");
        }

        if (HasIncompleteTrailingBullet(normalized))
        {
            return new TranscriptSummaryValidationResult(false, "Transcript summary output ended with an incomplete bullet.");
        }

        return TranscriptSummaryValidationResult.Valid;
    }

    private static bool ContainsUnsupportedOutput(string content)
    {
        return content.Contains("<think>", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("</think>", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("```", StringComparison.Ordinal) ||
            content.Contains("[end of text]", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("I cannot summarize", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("I can't summarize", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeInstructionEcho(string content)
    {
        return content.Contains("Output sections", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Source transcript:", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Do not add facts", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("Task:", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountMarkdownHeadings(string content)
    {
        return content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(static line => line.TrimStart().StartsWith("## ", StringComparison.Ordinal));
    }

    private static bool HasRepeatedMarkdownHeading(string content)
    {
        return content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .Where(static line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(static line => line[3..].Trim().ToUpperInvariant())
            .GroupBy(static heading => heading, StringComparer.Ordinal)
            .Any(static group => group.Count() > 1);
    }

    private static bool HasIncompleteTrailingBullet(string content)
    {
        var lastLine = content
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .LastOrDefault(static line => line.Length > 0);
        if (lastLine is null ||
            (!lastLine.StartsWith("- ", StringComparison.Ordinal) && !lastLine.StartsWith("* ", StringComparison.Ordinal)))
        {
            return false;
        }

        var bullet = lastLine[2..].Trim();
        return bullet.Length < 16 && ContainsJapaneseText(bullet) && !EndsLikeCompleteSentence(bullet);
    }

    private static bool EndsLikeCompleteSentence(string text)
    {
        return text.EndsWith('。') ||
            text.EndsWith('.') ||
            text.EndsWith('!') ||
            text.EndsWith('?') ||
            text.EndsWith('！') ||
            text.EndsWith('？');
    }

    private static bool ContainsJapaneseText(string text)
    {
        return text.Any(static ch =>
            (ch >= '\u3040' && ch <= '\u30ff') ||
            (ch >= '\u3400' && ch <= '\u9fff'));
    }
}
