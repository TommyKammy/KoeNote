namespace KoeNote.App.Services.Asr;

public sealed class AsrCommandBuilder
{
    public string BuildArguments(AsrRunOptions options)
    {
        return string.Join(" ", BuildArgumentList(options).Select(QuoteForDisplay));
    }

    public IReadOnlyList<string> BuildArgumentList(AsrRunOptions options)
    {
        var arguments = new List<string>();
        Append(arguments, "--model", options.ModelPath);
        Append(arguments, "--audio", options.NormalizedAudioPath);
        Append(arguments, "--format", "json");

        if (!string.IsNullOrWhiteSpace(options.Context))
        {
            Append(arguments, "--context", options.Context);
        }

        if (options.Hotwords is not null)
        {
            foreach (var hotword in options.Hotwords.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                Append(arguments, "--hotword", hotword);
            }
        }

        return arguments;
    }

    private static void Append(List<string> arguments, string name, string value)
    {
        arguments.Add(name);
        arguments.Add(value);
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
