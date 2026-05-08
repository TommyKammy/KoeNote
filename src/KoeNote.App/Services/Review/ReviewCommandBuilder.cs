using KoeNote.App.Services.Llm;

namespace KoeNote.App.Services.Review;

public sealed class ReviewCommandBuilder
{
    public string BuildArguments(ReviewRunOptions options, string promptFilePath, string? jsonSchemaFilePath = null)
    {
        return string.Join(" ", BuildArgumentList(options, promptFilePath, jsonSchemaFilePath).Select(QuoteForDisplay));
    }

    public IReadOnlyList<string> BuildArgumentList(ReviewRunOptions options, string promptFilePath, string? jsonSchemaFilePath = null)
    {
        return LlamaCompletionArgumentBuilder.Build(new LlamaCompletionArgumentOptions(
            options.ModelPath,
            promptFilePath,
            options.ContextSize,
            options.GpuLayers,
            options.MaxTokens,
            options.Temperature,
            options.Threads,
            options.ThreadsBatch,
            options.NoConversation,
            options.TopP,
            options.TopK,
            options.RepeatPenalty,
            jsonSchemaFilePath));
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
