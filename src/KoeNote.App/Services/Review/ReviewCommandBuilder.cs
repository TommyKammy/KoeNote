namespace KoeNote.App.Services.Review;

public sealed class ReviewCommandBuilder
{
    public string BuildArguments(ReviewRunOptions options, string promptFilePath, string? jsonSchemaFilePath = null)
    {
        return string.Join(" ", BuildArgumentList(options, promptFilePath, jsonSchemaFilePath).Select(QuoteForDisplay));
    }

    public IReadOnlyList<string> BuildArgumentList(ReviewRunOptions options, string promptFilePath, string? jsonSchemaFilePath = null)
    {
        var arguments = new List<string>
        {
            "--model",
            options.ModelPath,
            "--file",
            promptFilePath,
            "--ctx-size",
            "8192",
            "--n-gpu-layers",
            "999",
            "--n-predict",
            "4096",
            "--temp",
            "0.1",
            "--single-turn",
            "--no-display-prompt"
        };

        if (!string.IsNullOrWhiteSpace(jsonSchemaFilePath))
        {
            arguments.Add("--json-schema-file");
            arguments.Add(jsonSchemaFilePath);
        }

        return arguments;
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
