using System.IO;

namespace KoeNote.App.Services.Asr;

public static class AsrWorkerCommandBuilder
{
    public static IReadOnlyList<string> BuildArguments(
        AsrInput input,
        AsrEngineConfig config,
        AsrOptions options,
        string outputJsonPath,
        bool disableWorkerDiarization)
    {
        var arguments = new List<string>
        {
            config.WorkerPath!,
            "--audio",
            input.NormalizedAudioPath,
            "--model",
            config.ModelPath,
            "--output-json",
            outputJsonPath,
            "--language",
            "ja"
        };

        if (disableWorkerDiarization)
        {
            arguments.Add("--diarization");
            arguments.Add("off");
        }

        if (!string.IsNullOrWhiteSpace(options.Context))
        {
            arguments.Add("--context");
            arguments.Add(options.Context);
        }

        foreach (var hotword in options.Hotwords ?? [])
        {
            arguments.Add("--hotword");
            arguments.Add(hotword);
        }

        if (config.ModelId.Equals("kotoba-whisper-v2.2-faster", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("--device");
            arguments.Add("auto");
            arguments.Add("--compute-type");
            arguments.Add("float32");
            arguments.Add("--local-files-only");
            arguments.Add("--chunk-length");
            arguments.Add("5");
            arguments.Add("--condition-on-previous-text");
            arguments.Add("false");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(options.Device))
            {
                arguments.Add("--device");
                arguments.Add(options.Device);
            }

            if (!string.IsNullOrWhiteSpace(options.ComputeType))
            {
                arguments.Add("--compute-type");
                arguments.Add(options.ComputeType);
            }

            if (!string.IsNullOrWhiteSpace(options.ExecutionProfileId))
            {
                arguments.Add("--execution-profile");
                arguments.Add(options.ExecutionProfileId);
            }

            arguments.Add("--attempt-number");
            arguments.Add(Math.Max(1, options.AttemptNumber).ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (options.ChunkSeconds is > 0)
            {
                arguments.Add("--chunk-seconds");
                arguments.Add(options.ChunkSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return arguments;
    }

    public static bool IsCommandName(string value)
    {
        return !value.Contains(Path.DirectorySeparatorChar)
            && !value.Contains(Path.AltDirectorySeparatorChar)
            && !Path.IsPathRooted(value);
    }

    public static string GetArgumentValue(IReadOnlyList<string> arguments, string optionName, string fallback)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index].Equals(optionName, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return fallback;
    }

    public static string BuildArgumentSummary(IReadOnlyList<string> arguments)
    {
        var sanitized = new List<string>(arguments.Count);
        for (var index = 0; index < arguments.Count; index++)
        {
            sanitized.Add(arguments[index]);
            if (arguments[index].Equals("--context", StringComparison.OrdinalIgnoreCase) ||
                arguments[index].Equals("--hotword", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < arguments.Count)
                {
                    sanitized.Add("(redacted)");
                    index++;
                }
            }
        }

        return string.Join(" ", sanitized.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }
}
