using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.IO;
using KoeNote.App.Services.Jobs;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Export;

public sealed class TranscriptExportService(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public TranscriptExportResult ExportJob(
        string jobId,
        string outputDirectory,
        IReadOnlyCollection<TranscriptExportFormat>? formats = null)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        var snapshot = LoadSnapshot(jobId);
        if (snapshot.Segments.Count == 0)
        {
            throw new InvalidOperationException($"No transcript segments were available for export: {jobId}");
        }

        Directory.CreateDirectory(outputDirectory);
        var selectedFormats = formats is { Count: > 0 }
            ? formats
            : [TranscriptExportFormat.Text, TranscriptExportFormat.Markdown, TranscriptExportFormat.Json, TranscriptExportFormat.Srt, TranscriptExportFormat.Vtt];
        var filePaths = new List<string>();
        var baseName = SanitizeFileName(snapshot.Title);

        foreach (var format in selectedFormats.Distinct())
        {
            var path = Path.Combine(outputDirectory, $"{baseName}.{GetExtension(format)}");
            File.WriteAllText(path, Render(snapshot, format), Encoding.UTF8);
            filePaths.Add(path);
        }

        var result = new TranscriptExportResult(
            jobId,
            outputDirectory,
            filePaths,
            snapshot.Segments.Count,
            snapshot.PendingDraftCount,
            snapshot.PendingDraftCount > 0);

        var level = result.HasUnresolvedDrafts ? "warning" : "info";
        var warning = result.HasUnresolvedDrafts ? $" ({result.PendingDraftCount} unresolved drafts)" : "";
        new JobLogRepository(paths).AddEvent(jobId, "export", level, $"Exported transcript files to {outputDirectory}{warning}");
        return result;
    }

    private ExportSnapshot LoadSnapshot(string jobId)
    {
        using var connection = OpenConnection();
        var title = LoadJobTitle(connection, jobId);
        var pendingDraftCount = LoadPendingDraftCount(connection, jobId);
        var segments = LoadSegments(connection, jobId);
        return new ExportSnapshot(jobId, title, pendingDraftCount, segments);
    }

    private static string LoadJobTitle(SqliteConnection connection, string jobId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", jobId);
        return Convert.ToString(command.ExecuteScalar()) ?? jobId;
    }

    private static int LoadPendingDraftCount(SqliteConnection connection, string jobId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM correction_drafts
            WHERE job_id = $job_id AND status = 'pending';
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static IReadOnlyList<TranscriptExportSegment> LoadSegments(SqliteConnection connection, string jobId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                s.segment_id,
                s.start_seconds,
                s.end_seconds,
                COALESCE(a.display_name, s.speaker_name, s.speaker_id, ''),
                COALESCE(s.final_text, s.normalized_text, s.raw_text)
            FROM transcript_segments s
            LEFT JOIN speaker_aliases a
                ON a.job_id = s.job_id AND a.speaker_id = s.speaker_id
            WHERE s.job_id = $job_id
            ORDER BY s.start_seconds ASC, s.end_seconds ASC;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);

        using var reader = command.ExecuteReader();
        var segments = new List<TranscriptExportSegment>();
        while (reader.Read())
        {
            segments.Add(new TranscriptExportSegment(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return segments;
    }

    private static string Render(ExportSnapshot snapshot, TranscriptExportFormat format)
    {
        return format switch
        {
            TranscriptExportFormat.Text => RenderText(snapshot),
            TranscriptExportFormat.Markdown => RenderMarkdown(snapshot),
            TranscriptExportFormat.Json => RenderJson(snapshot),
            TranscriptExportFormat.Srt => RenderSrt(snapshot),
            TranscriptExportFormat.Vtt => RenderVtt(snapshot),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static string RenderText(ExportSnapshot snapshot)
    {
        var builder = new StringBuilder();
        foreach (var segment in snapshot.Segments)
        {
            builder.Append('[')
                .Append(FormatTimestamp(segment.StartSeconds))
                .Append(" - ")
                .Append(FormatTimestamp(segment.EndSeconds))
                .Append("] ");
            if (!string.IsNullOrWhiteSpace(segment.Speaker))
            {
                builder.Append(segment.Speaker).Append(": ");
            }

            builder.AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string RenderMarkdown(ExportSnapshot snapshot)
    {
        var builder = new StringBuilder()
            .Append("# ")
            .AppendLine(snapshot.Title)
            .AppendLine();
        if (snapshot.PendingDraftCount > 0)
        {
            builder.Append("> Warning: ")
                .Append(snapshot.PendingDraftCount)
                .AppendLine(" unresolved correction drafts remain.")
                .AppendLine();
        }

        foreach (var segment in snapshot.Segments)
        {
            builder.Append("- `")
                .Append(FormatTimestamp(segment.StartSeconds))
                .Append("` ");
            if (!string.IsNullOrWhiteSpace(segment.Speaker))
            {
                builder.Append("**").Append(segment.Speaker).Append("**: ");
            }

            builder.AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string RenderJson(ExportSnapshot snapshot)
    {
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

    private static string RenderSrt(ExportSnapshot snapshot)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            var segment = snapshot.Segments[i];
            builder.AppendLine((i + 1).ToString());
            builder.Append(FormatSubtitleTimestamp(segment.StartSeconds, comma: true))
                .Append(" --> ")
                .AppendLine(FormatSubtitleTimestamp(segment.EndSeconds, comma: true));
            builder.AppendLine(FormatSubtitleText(segment));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string RenderVtt(ExportSnapshot snapshot)
    {
        var builder = new StringBuilder("WEBVTT").AppendLine().AppendLine();
        foreach (var segment in snapshot.Segments)
        {
            builder.Append(FormatSubtitleTimestamp(segment.StartSeconds, comma: false))
                .Append(" --> ")
                .AppendLine(FormatSubtitleTimestamp(segment.EndSeconds, comma: false));
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

    private static string FormatTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private static string FormatSubtitleTimestamp(double seconds, bool comma)
    {
        var time = TimeSpan.FromSeconds(seconds);
        var separator = comma ? "," : ".";
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{separator}{time.Milliseconds:000}";
    }

    private static string GetExtension(TranscriptExportFormat format)
    {
        return format switch
        {
            TranscriptExportFormat.Text => "txt",
            TranscriptExportFormat.Markdown => "md",
            TranscriptExportFormat.Json => "json",
            TranscriptExportFormat.Srt => "srt",
            TranscriptExportFormat.Vtt => "vtt",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "transcript" : sanitized;
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }

    private sealed record ExportSnapshot(
        string JobId,
        string Title,
        int PendingDraftCount,
        IReadOnlyList<TranscriptExportSegment> Segments);
}
