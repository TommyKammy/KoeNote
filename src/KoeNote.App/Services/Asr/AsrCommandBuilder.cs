using System.Text;

namespace KoeNote.App.Services.Asr;

public sealed class AsrCommandBuilder
{
    public string BuildArguments(AsrRunOptions options)
    {
        var builder = new StringBuilder();
        Append(builder, "--model", options.ModelPath);
        Append(builder, "--audio", options.NormalizedAudioPath);
        Append(builder, "--format", "json");

        if (!string.IsNullOrWhiteSpace(options.Context))
        {
            Append(builder, "--context", options.Context);
        }

        if (options.Hotwords is not null)
        {
            foreach (var hotword in options.Hotwords.Where(static value => !string.IsNullOrWhiteSpace(value)))
            {
                Append(builder, "--hotword", hotword);
            }
        }

        return builder.ToString().Trim();
    }

    private static void Append(StringBuilder builder, string name, string value)
    {
        builder.Append(name);
        builder.Append(' ');
        builder.Append(Quote(value));
        builder.Append(' ');
    }

    private static string Quote(string value)
    {
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
