namespace KoeNote.App.Services.Export;

internal static class TranscriptExportSegmentMerger
{
    public static TranscriptExportSnapshot ApplyFormatOptions(
        TranscriptExportSnapshot snapshot,
        TranscriptExportFormat format,
        TranscriptExportOptions options)
    {
        return options.MergeConsecutiveSpeakers && SupportsSpeakerMerging(format)
            ? snapshot with { Segments = MergeConsecutiveSpeakers(snapshot.Segments) }
            : snapshot;
    }

    private static bool SupportsSpeakerMerging(TranscriptExportFormat format)
    {
        return format is TranscriptExportFormat.Text
            or TranscriptExportFormat.Markdown
            or TranscriptExportFormat.Docx
            or TranscriptExportFormat.Xlsx;
    }

    private static IReadOnlyList<TranscriptExportSegment> MergeConsecutiveSpeakers(
        IReadOnlyList<TranscriptExportSegment> segments)
    {
        if (segments.Count <= 1)
        {
            return segments;
        }

        var merged = new List<TranscriptExportSegment>();
        TranscriptExportSegment? current = null;

        foreach (var segment in segments)
        {
            if (current is not null && CanMerge(current, segment))
            {
                current = Merge(current, segment);
                continue;
            }

            if (current is not null)
            {
                merged.Add(current);
            }

            current = segment;
        }

        if (current is not null)
        {
            merged.Add(current);
        }

        return merged;
    }

    private static bool CanMerge(TranscriptExportSegment left, TranscriptExportSegment right)
    {
        return !string.IsNullOrWhiteSpace(left.Speaker)
            && string.Equals(left.Speaker, right.Speaker, StringComparison.Ordinal);
    }

    private static TranscriptExportSegment Merge(TranscriptExportSegment left, TranscriptExportSegment right)
    {
        return left with
        {
            SegmentId = $"{left.SegmentId}+{right.SegmentId}",
            EndSeconds = right.EndSeconds,
            Text = JoinText(left.Text, right.Text),
            RawText = JoinText(left.RawText, right.RawText),
            PolishedText = JoinText(left.PolishedText, right.PolishedText)
        };
    }

    private static string JoinText(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left))
        {
            return string.IsNullOrWhiteSpace(right) ? string.Empty : right;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            return left;
        }

        return string.Concat(left, Environment.NewLine, right);
    }
}
