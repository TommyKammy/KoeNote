using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KoeNote.App.Services.Export;

internal static class TranscriptExportContentRenderer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public static string Render(
        TranscriptExportSnapshot snapshot,
        TranscriptExportFormat format,
        TranscriptExportOptions options)
    {
        return format switch
        {
            TranscriptExportFormat.Text => RenderText(snapshot, options),
            TranscriptExportFormat.Markdown => RenderMarkdown(snapshot, options),
            TranscriptExportFormat.Json => RenderJson(snapshot),
            TranscriptExportFormat.Srt => RenderSrt(snapshot),
            TranscriptExportFormat.Vtt => RenderVtt(snapshot),
            TranscriptExportFormat.Docx => RenderText(snapshot, options),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static string RenderText(TranscriptExportSnapshot snapshot, TranscriptExportOptions options)
    {
        if (snapshot.DocumentContent is { } documentContent)
        {
            return documentContent.TrimEnd() + Environment.NewLine;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            if (options.MergeConsecutiveSpeakers && i > 0)
            {
                builder.AppendLine();
            }

            var segment = snapshot.Segments[i];
            AppendDisplayTimestamp(builder, segment, options);
            if (!string.IsNullOrWhiteSpace(segment.Speaker))
            {
                builder.Append(segment.Speaker).Append(": ");
            }

            builder.AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string RenderMarkdown(TranscriptExportSnapshot snapshot, TranscriptExportOptions options)
    {
        if (snapshot.DocumentContent is { } documentContent)
        {
            return documentContent.TrimEnd() + Environment.NewLine;
        }

        var builder = new StringBuilder()
            .Append("# ")
            .AppendLine(snapshot.Title)
            .AppendLine();
        if (snapshot.PendingDraftCount > 0)
        {
            builder.Append("> Warning: ")
                .Append(snapshot.PendingDraftCount)
                .AppendLine("件の未処理の整文候補が残っています。")
                .AppendLine();
        }

        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            if (options.MergeConsecutiveSpeakers && i > 0)
            {
                builder.AppendLine();
            }

            var segment = snapshot.Segments[i];
            builder.Append("- ");
            if (options.IncludeTimestamps)
            {
                builder.Append('`')
                    .Append(TimestampFormatter.FormatDisplay(segment.StartSeconds))
                    .Append("` ");
            }

            if (!string.IsNullOrWhiteSpace(segment.Speaker))
            {
                builder.Append("**").Append(segment.Speaker).Append("**: ");
            }

            builder.AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string RenderJson(TranscriptExportSnapshot snapshot)
    {
        if (snapshot.DocumentContent is not null)
        {
            throw new InvalidOperationException("Readable polished transcript export does not support JSON.");
        }

        var payload = new
        {
            snapshot.JobId,
            snapshot.Title,
            snapshot.PendingDraftCount,
            HasUnresolvedDrafts = snapshot.PendingDraftCount > 0,
            Segments = snapshot.Segments
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static string RenderSrt(TranscriptExportSnapshot snapshot)
    {
        if (snapshot.DocumentContent is not null)
        {
            throw new InvalidOperationException("Readable polished transcript export does not support SRT.");
        }

        var builder = new StringBuilder();
        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            var segment = snapshot.Segments[i];
            builder.AppendLine((i + 1).ToString());
            builder.Append(TimestampFormatter.FormatSrt(segment.StartSeconds))
                .Append(" --> ")
                .AppendLine(TimestampFormatter.FormatSrt(segment.EndSeconds));
            builder.AppendLine(FormatSubtitleText(segment));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string RenderVtt(TranscriptExportSnapshot snapshot)
    {
        if (snapshot.DocumentContent is not null)
        {
            throw new InvalidOperationException("Readable polished transcript export does not support VTT.");
        }

        var builder = new StringBuilder("WEBVTT").AppendLine().AppendLine();
        foreach (var segment in snapshot.Segments)
        {
            builder.Append(TimestampFormatter.FormatVtt(segment.StartSeconds))
                .Append(" --> ")
                .AppendLine(TimestampFormatter.FormatVtt(segment.EndSeconds));
            builder.AppendLine(FormatSubtitleText(segment));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string FormatSubtitleText(TranscriptExportSegment segment)
    {
        var text = NormalizeSubtitleCueText(segment.Text);
        return string.IsNullOrWhiteSpace(segment.Speaker)
            ? text
            : $"{segment.Speaker}: {text}";
    }

    private static string NormalizeSubtitleCueText(string text)
    {
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0);
        var normalized = string.Join(Environment.NewLine, lines);
        return string.IsNullOrWhiteSpace(normalized)
            ? string.Empty
            : normalized;
    }

    private static void AppendDisplayTimestamp(
        StringBuilder builder,
        TranscriptExportSegment segment,
        TranscriptExportOptions options)
    {
        if (!options.IncludeTimestamps)
        {
            return;
        }

        builder.Append('[')
            .Append(TimestampFormatter.FormatDisplay(segment.StartSeconds))
            .Append(" - ")
            .Append(TimestampFormatter.FormatDisplay(segment.EndSeconds))
            .Append("] ");
    }
}
