namespace KoeNote.App.Services.Llm;

public sealed record LlamaCompletionArgumentOptions(
    string ModelPath,
    string PromptFilePath,
    int ContextSize,
    int GpuLayers,
    int MaxTokens,
    double Temperature,
    int? Threads = null,
    int? ThreadsBatch = null,
    bool NoConversation = true,
    double? TopP = null,
    int? TopK = null,
    double? RepeatPenalty = null,
    string? JsonSchemaFilePath = null);

public static class LlamaCompletionArgumentBuilder
{
    public static IReadOnlyList<string> Build(LlamaCompletionArgumentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var arguments = new List<string>
        {
            "--model",
            options.ModelPath,
            "--file",
            options.PromptFilePath,
            "--ctx-size",
            Format(options.ContextSize),
            "--n-gpu-layers",
            Format(options.GpuLayers),
            "--n-predict",
            Format(options.MaxTokens),
            "--temp",
            Format(options.Temperature)
        };

        if (options.NoConversation)
        {
            arguments.Add("--no-conversation");
        }

        arguments.Add("--no-display-prompt");

        if (options.TopP is { } topP)
        {
            arguments.Add("--top-p");
            arguments.Add(Format(topP));
        }

        if (options.TopK is { } topK)
        {
            arguments.Add("--top-k");
            arguments.Add(Format(topK));
        }

        if (options.RepeatPenalty is { } repeatPenalty)
        {
            arguments.Add("--repeat-penalty");
            arguments.Add(Format(repeatPenalty));
        }

        if (options.Threads is { } threads)
        {
            arguments.Add("--threads");
            arguments.Add(Format(threads));
        }

        if (options.ThreadsBatch is { } threadsBatch)
        {
            arguments.Add("--threads-batch");
            arguments.Add(Format(threadsBatch));
        }

        if (!string.IsNullOrWhiteSpace(options.JsonSchemaFilePath))
        {
            arguments.Add("--json-schema-file");
            arguments.Add(options.JsonSchemaFilePath);
        }

        return arguments;
    }

    private static string Format(int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Format(double value)
    {
        return value.ToString("0.###############", System.Globalization.CultureInfo.InvariantCulture);
    }
}
