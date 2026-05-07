using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.IO;
using System.IO.Compression;
using System.Security;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Transcript;
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
        return ExportJob(jobId, outputDirectory, formats, new TranscriptExportOptions());
    }

    public TranscriptExportResult ExportJob(
        string jobId,
        string outputDirectory,
        IReadOnlyCollection<TranscriptExportFormat>? formats,
        TranscriptExportOptions? options)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        var exportOptions = options ?? new TranscriptExportOptions();
        var snapshot = LoadSnapshot(jobId, exportOptions.Source);
        if (snapshot.Segments.Count == 0)
        {
            throw new InvalidOperationException($"No transcript segments were available for export: {jobId}");
        }

        Directory.CreateDirectory(outputDirectory);
        var selectedFormats = formats is { Count: > 0 }
            ? formats
            : [TranscriptExportFormat.Text, TranscriptExportFormat.Markdown, TranscriptExportFormat.Json, TranscriptExportFormat.Srt, TranscriptExportFormat.Vtt];
        var filePaths = new List<string>();
        var baseName = SanitizeFileName(string.IsNullOrWhiteSpace(exportOptions.BaseFileName)
            ? snapshot.Title
            : exportOptions.BaseFileName);

        foreach (var format in selectedFormats.Distinct())
        {
            var path = Path.Combine(outputDirectory, $"{baseName}.{GetExtension(format)}");
            WriteFormat(path, snapshot, format, exportOptions);
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
        new JobLogRepository(paths).AddEvent(
            jobId,
            "export",
            level,
            $"Exported {GetSourceLogName(exportOptions.Source)} transcript files to {outputDirectory}{warning}");
        return result;
    }

    public TranscriptExportResult ExportJobToFile(
        string jobId,
        string outputPath,
        TranscriptExportFormat format,
        TranscriptExportOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id is required.", nameof(jobId));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("Output path is required.", nameof(outputPath));
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output path must include a directory.", nameof(outputPath));
        }

        var exportOptions = options ?? new TranscriptExportOptions();
        var snapshot = LoadSnapshot(jobId, exportOptions.Source);
        if (snapshot.Segments.Count == 0)
        {
            throw new InvalidOperationException($"No transcript segments were available for export: {jobId}");
        }

        Directory.CreateDirectory(outputDirectory);
        WriteFormat(outputPath, snapshot, format, exportOptions);

        var result = new TranscriptExportResult(
            jobId,
            outputDirectory,
            [outputPath],
            snapshot.Segments.Count,
            snapshot.PendingDraftCount,
            snapshot.PendingDraftCount > 0);

        var level = result.HasUnresolvedDrafts ? "warning" : "info";
        var warning = result.HasUnresolvedDrafts ? $" ({result.PendingDraftCount} unresolved drafts)" : "";
        new JobLogRepository(paths).AddEvent(
            jobId,
            "export",
            level,
            $"Exported {GetSourceLogName(exportOptions.Source)} transcript file to {outputPath}{warning}");
        return result;
    }

    private ExportSnapshot LoadSnapshot(string jobId, TranscriptExportSource source)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        var title = LoadJobTitle(connection, jobId);
        var pendingDraftCount = LoadPendingDraftCount(connection, jobId);
        var segments = new TranscriptReadRepository(paths)
            .ReadForJob(jobId)
            .Select(segment => new TranscriptExportSegment(
                segment.SegmentId,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Speaker,
                SelectExportText(segment, source)))
            .ToArray();
        return new ExportSnapshot(jobId, title, pendingDraftCount, segments);
    }

    private static string SelectExportText(TranscriptReadModel segment, TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => segment.RawText,
            TranscriptExportSource.Polished => segment.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private static string GetSourceLogName(TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => "raw",
            TranscriptExportSource.Polished => "polished",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
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

    private static string Render(ExportSnapshot snapshot, TranscriptExportFormat format, TranscriptExportOptions options)
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

    private static void WriteFormat(string path, ExportSnapshot snapshot, TranscriptExportFormat format, TranscriptExportOptions options)
    {
        if (format == TranscriptExportFormat.Docx)
        {
            WriteDocx(path, snapshot, options);
            return;
        }

        File.WriteAllText(path, Render(snapshot, format, options), Encoding.UTF8);
    }

    private static string RenderText(ExportSnapshot snapshot, TranscriptExportOptions options)
    {
        var builder = new StringBuilder();
        foreach (var segment in snapshot.Segments)
        {
            AppendDisplayTimestamp(builder, segment, options);
            if (!string.IsNullOrWhiteSpace(segment.Speaker))
            {
                builder.Append(segment.Speaker).Append(": ");
            }

            builder.AppendLine(segment.Text);
        }

        return builder.ToString();
    }

    private static string RenderMarkdown(ExportSnapshot snapshot, TranscriptExportOptions options)
    {
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

        foreach (var segment in snapshot.Segments)
        {
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
            builder.Append(TimestampFormatter.FormatSrt(segment.StartSeconds))
                .Append(" --> ")
                .AppendLine(TimestampFormatter.FormatSrt(segment.EndSeconds));
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

    private static string GetExtension(TranscriptExportFormat format)
    {
        return format switch
        {
            TranscriptExportFormat.Text => "txt",
            TranscriptExportFormat.Markdown => "md",
            TranscriptExportFormat.Json => "json",
            TranscriptExportFormat.Srt => "srt",
            TranscriptExportFormat.Vtt => "vtt",
            TranscriptExportFormat.Docx => "docx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static void WriteDocx(string path, ExportSnapshot snapshot, TranscriptExportOptions options)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteZipEntry(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/>
            </Types>
            """);
        WriteZipEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/>
            </Relationships>
            """);
        WriteZipEntry(archive, "word/document.xml", RenderDocxDocument(snapshot, options));
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string RenderDocxDocument(ExportSnapshot snapshot, TranscriptExportOptions options)
    {
        var builder = new StringBuilder()
            .AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""")
            .AppendLine("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""")
            .AppendLine("<w:body>")
            .Append("<w:p><w:r><w:t>")
            .Append(SecurityElement.Escape(snapshot.Title))
            .AppendLine("</w:t></w:r></w:p>");

        if (snapshot.PendingDraftCount > 0)
        {
            builder.Append("<w:p><w:r><w:t>")
                .Append(SecurityElement.Escape($"{snapshot.PendingDraftCount}件の未処理の整文候補が残っています。"))
                .AppendLine("</w:t></w:r></w:p>");
        }

        foreach (var segment in snapshot.Segments)
        {
            var prefix = options.IncludeTimestamps
                ? $"[{TimestampFormatter.FormatDisplay(segment.StartSeconds)} - {TimestampFormatter.FormatDisplay(segment.EndSeconds)}] "
                : string.Empty;
            if (!string.IsNullOrWhiteSpace(segment.Speaker))
            {
                prefix += $"{segment.Speaker}: ";
            }

            builder.Append("<w:p><w:r><w:t>")
                .Append(SecurityElement.Escape(prefix + segment.Text))
                .AppendLine("</w:t></w:r></w:p>");
        }

        return builder
            .AppendLine("<w:sectPr/>")
            .AppendLine("</w:body>")
            .AppendLine("</w:document>")
            .ToString();
    }

    private static void AppendDisplayTimestamp(StringBuilder builder, TranscriptExportSegment segment, TranscriptExportOptions options)
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

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "transcript" : sanitized;
    }

    private sealed record ExportSnapshot(
        string JobId,
        string Title,
        int PendingDraftCount,
        IReadOnlyList<TranscriptExportSegment> Segments);
}
