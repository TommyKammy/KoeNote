using System.IO;

namespace KoeNote.App.Services.Asr;

public sealed class AsrCommandBuilder
{
    public static string GetJsonOutputPath(AsrRunOptions options)
    {
        return $"{GetOutputFileBase(options)}.json";
    }

    public string BuildArguments(AsrRunOptions options)
    {
        return string.Join(" ", BuildArgumentList(options).Select(QuoteForDisplay));
    }

    public IReadOnlyList<string> BuildArgumentList(AsrRunOptions options)
    {
        var arguments = new List<string>();
        Append(arguments, "--backend", "vibevoice");
        Append(arguments, "--gpu-backend", "cuda");
        Append(arguments, "--model", options.ModelPath);
        Append(arguments, "--file", options.NormalizedAudioPath);
        Append(arguments, "--language", "ja");
        arguments.Add("--no-punctuation");
        arguments.Add("--output-json");
        Append(arguments, "--output-file", GetOutputFileBase(options));

        var prompt = BuildPrompt(options);
        if (!string.IsNullOrWhiteSpace(prompt))
        {
            Append(arguments, "--prompt", prompt);
        }

        return arguments;
    }

    private static void Append(List<string> arguments, string name, string value)
    {
        arguments.Add(name);
        arguments.Add(value);
    }

    private static string GetOutputFileBase(AsrRunOptions options)
    {
        return Path.Combine(options.OutputDirectory, "crispasr");
    }

    private static string? BuildPrompt(AsrRunOptions options)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.Context))
        {
            parts.Add(options.Context.Trim());
        }

        if (options.Hotwords is not null)
        {
            var hotwords = options.Hotwords
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .ToArray();

            if (hotwords.Length > 0)
            {
                parts.Add($"Keywords: {string.Join(", ", hotwords)}");
            }
        }

        return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts);
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
