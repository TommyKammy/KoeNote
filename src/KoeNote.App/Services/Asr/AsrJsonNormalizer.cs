using System.Globalization;
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
                var text = ReadString(element, "text", "raw_text", "transcript");
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var segmentId = ReadString(element, "segment_id", "id") ?? index.ToString("D6");
                var start = ReadDouble(element, "start_seconds", "start", "start_time") ?? 0;
                var end = ReadDouble(element, "end_seconds", "end", "end_time") ?? start;
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

        foreach (var name in new[] { "segments", "result", "results" })
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
}
