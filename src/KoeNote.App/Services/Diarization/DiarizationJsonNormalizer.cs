using System.Globalization;
using System.Text.Json;

namespace KoeNote.App.Services.Diarization;

public sealed class DiarizationJsonNormalizer
{
    public DiarizationWorkerOutput Normalize(string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        var status = ReadString(root, "status") ?? "unknown";
        var turns = new List<DiarizationTurn>();

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("turns", out var turnsElement) &&
            turnsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in turnsElement.EnumerateArray())
            {
                var start = ReadDouble(item, "start", "start_seconds", "start_time");
                var end = ReadDouble(item, "end", "end_seconds", "end_time");
                var speaker = ReadString(item, "speaker", "speaker_id", "label");

                if (start is null || end is null || end <= start || string.IsNullOrWhiteSpace(speaker))
                {
                    continue;
                }

                turns.Add(new DiarizationTurn(start.Value, end.Value, NormalizeSpeakerId(speaker)));
            }
        }

        return new DiarizationWorkerOutput(status, turns);
    }

    private static string NormalizeSpeakerId(string speaker)
    {
        var value = speaker.Trim();
        if (value.StartsWith("Speaker_", StringComparison.OrdinalIgnoreCase))
        {
            return "Speaker_" + value["Speaker_".Length..];
        }

        if (value.StartsWith("SPEAKER_", StringComparison.OrdinalIgnoreCase))
        {
            return "Speaker_" + value["SPEAKER_".Length..];
        }

        return value;
    }

    private static string? ReadString(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static double? ReadDouble(JsonElement element, params string[] propertyNames)
    {
        var value = ReadString(element, propertyNames);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
