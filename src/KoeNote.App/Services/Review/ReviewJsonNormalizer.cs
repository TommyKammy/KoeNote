using System.Globalization;
using System.Text.Json;
using KoeNote.App.Models;

namespace KoeNote.App.Services.Review;

public sealed class ReviewJsonNormalizer
{
    public IReadOnlyList<CorrectionDraft> Normalize(
        string jobId,
        IReadOnlyList<TranscriptSegment> sourceSegments,
        string rawJson,
        double minConfidence)
    {
        try
        {
            using var document = JsonDocument.Parse(rawJson);
            var elements = FindDraftElements(document.RootElement);
            var segmentsById = sourceSegments.ToDictionary(segment => segment.SegmentId, StringComparer.Ordinal);
            var drafts = new List<CorrectionDraft>();
            var index = 1;

            foreach (var element in elements)
            {
                var segmentId = ReadString(element, "segment_id", "segmentId");
                if (string.IsNullOrWhiteSpace(segmentId) || !segmentsById.TryGetValue(segmentId, out var sourceSegment))
                {
                    continue;
                }

                var originalText = ReadString(element, "original_text", "originalText", "original")?.Trim();
                var suggestedText = ReadString(element, "suggested_text", "suggestedText", "suggestion")?.Trim();
                var issueType = ReadString(element, "issue_type", "issueType", "type")?.Trim();
                var reason = ReadString(element, "reason")?.Trim();
                var confidence = ReadDouble(element, "confidence") ?? 0;

                if (string.IsNullOrWhiteSpace(originalText)
                    || string.IsNullOrWhiteSpace(suggestedText)
                    || string.IsNullOrWhiteSpace(issueType)
                    || string.IsNullOrWhiteSpace(reason)
                    || confidence < minConfidence
                    || string.Equals(originalText, suggestedText, StringComparison.Ordinal)
                    || !ContainsOriginalText(sourceSegment, originalText)
                    || LooksLikeUnsupportedAddition(originalText, suggestedText))
                {
                    continue;
                }

                drafts.Add(new CorrectionDraft(
                    $"{segmentId}-{index:D2}",
                    jobId,
                    segmentId,
                    issueType,
                    originalText,
                    suggestedText,
                    reason,
                    confidence,
                    CreatedAt: DateTimeOffset.Now));
                index++;
            }

            return drafts;
        }
        catch (JsonException exception)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.JsonParseFailed, "Review output was not valid JSON.", exception);
        }
    }

    private static IEnumerable<JsonElement> FindDraftElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Array)
        {
            return root.EnumerateArray();
        }

        foreach (var name in new[] { "corrections", "drafts", "items", "results" })
        {
            if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Array)
            {
                return property.EnumerateArray();
            }
        }

        return [];
    }

    private static bool ContainsOriginalText(TranscriptSegment segment, string originalText)
    {
        var sourceText = segment.NormalizedText ?? segment.RawText;
        return sourceText.Contains(originalText, StringComparison.Ordinal);
    }

    private static bool LooksLikeUnsupportedAddition(string originalText, string suggestedText)
    {
        return suggestedText.Length > Math.Max(originalText.Length + 20, (int)(originalText.Length * 1.8));
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
