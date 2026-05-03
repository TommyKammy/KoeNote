namespace KoeNote.App.Services.Review;

public sealed class ReviewCommandBuilder
{
    public string BuildArguments(ReviewRunOptions options, string prompt)
    {
        return string.Join(" ", BuildArgumentList(options, prompt).Select(QuoteForDisplay));
    }

    public IReadOnlyList<string> BuildArgumentList(ReviewRunOptions options, string prompt)
    {
        return
        [
            "--model",
            options.ModelPath,
            "--prompt",
            prompt,
            "--ctx-size",
            "4096",
            "--n-predict",
            "1024",
            "--temp",
            "0.1"
        ];
    }

    private static string QuoteForDisplay(string value)
    {
        if (!string.IsNullOrEmpty(value) && !value.Any(char.IsWhiteSpace) && !value.Contains('"', StringComparison.Ordinal))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
