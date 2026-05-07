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
            options.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-gpu-layers",
            options.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-predict",
            options.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--temp",
            "0.1",
            "--single-turn",
            "--no-display-prompt"
        };

        if (options.Threads is { } threads)
        {
            arguments.Add("--threads");
            arguments.Add(threads.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options.ThreadsBatch is { } threadsBatch)
        {
            arguments.Add("--threads-batch");
            arguments.Add(threadsBatch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

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
