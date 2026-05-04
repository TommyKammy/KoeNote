using System.Globalization;
using System.Text.RegularExpressions;
using System.Text.Json;
using KoeNote.App.Models;

namespace KoeNote.App.Services.Asr;

public sealed class AsrJsonNormalizer
{
    public IReadOnlyList<TranscriptSegment> Normalize(string jobId, string rawJson)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var segmentElements = FindSegments(document.RootElement);
            var segments = new List<TranscriptSegment>();
            var index = 1;

            foreach (var element in segmentElements)
            {
                var text = NormalizeText(ReadString(element, "text", "raw_text", "transcript"));
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var segmentId = ReadString(element, "segment_id", "id") ?? index.ToString("D6");
                var start = ReadDouble(element, "start_seconds", "start", "start_time")
                    ?? ReadNestedOffsetSeconds(element, "from")
                    ?? 0;
                var end = ReadDouble(element, "end_seconds", "end", "end_time")
                    ?? ReadNestedOffsetSeconds(element, "to")
                    ?? start;
                var speaker = ReadString(element, "speaker_id", "speaker", "speaker_label");
                var confidence = ReadDouble(element, "asr_confidence", "confidence");

                segments.Add(new TranscriptSegment(
                    segmentId,
                    jobId,
                    start,
                    end,
                    speaker,
                    text,
                    NormalizedText: text,
                    AsrConfidence: confidence));

                index++;
            }

            if (segments.Count == 0)
            {
                throw new AsrWorkerException(AsrFailureCategory.NoSegments, "ASR JSON did not contain any usable segments.");
            }

            return segments;
        }
        catch (JsonException exception)
        {
            throw new AsrWorkerException(AsrFailureCategory.JsonParseFailed, "ASR output was not valid JSON.", exception);
        }
    }

    private static IEnumerable<JsonElement> FindSegments(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        foreach (var name in new[] { "segments", "transcription", "result", "results" })
        {
            if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray();
            }
        }

        return [];
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property))
            {
                return property.ValueKind switch
                {
                    JsonValueKind.String => property.GetString(),
                    JsonValueKind.Number => property.GetRawText(),
                    _ => null
                };
            }
        }

        return null;
    }

    private static double? ReadDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var property))
            {
                continue;
            }

            if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String
                && double.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static double? ReadNestedOffsetSeconds(JsonElement element, string name)
    {
        if (element.TryGetProperty("offsets", out var offsets)
            && offsets.ValueKind == JsonValueKind.Object
            && offsets.TryGetProperty(name, out var offset)
            && offset.ValueKind == JsonValueKind.Number
            && offset.TryGetDouble(out var milliseconds))
        {
            return milliseconds / 1000.0;
        }

        if (element.TryGetProperty("timestamps", out var timestamps)
            && timestamps.ValueKind == JsonValueKind.Object
            && timestamps.TryGetProperty(name, out var timestamp)
            && timestamp.ValueKind == JsonValueKind.String
            && TryParseTimestamp(timestamp.GetString(), out var seconds))
        {
            return seconds;
        }

        return null;
    }

    private static bool TryParseTimestamp(string? value, out double seconds)
    {
        seconds = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace(",", ".", StringComparison.Ordinal);
        var parts = normalized.Split(':');
        if (parts.Length != 3
            || !double.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hours)
            || !double.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes)
            || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var secondPart))
        {
            return false;
        }

        seconds = (hours * 3600) + (minutes * 60) + secondPart;
        return true;
    }

    private static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var normalized = text.Trim();
        normalized = Regex.Replace(normalized, @"Start\d+(?:\.\d+)?End\d+(?:\.\d+)?(?:Speaker\d+)?Content(?:Silence)?", string.Empty, RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\s{2,}", " ");
        return normalized.Trim();
    }
}
