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
        if (snapshot.Segments.Count == 0 && string.IsNullOrWhiteSpace(snapshot.DocumentContent))
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
        if (snapshot.Segments.Count == 0 && string.IsNullOrWhiteSpace(snapshot.DocumentContent))
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
        if (source == TranscriptExportSource.ReadablePolished)
        {
            var derivative = new TranscriptDerivativeRepository(paths)
                .ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished);
            if (derivative is null || string.IsNullOrWhiteSpace(derivative.Content))
            {
                throw new InvalidOperationException($"No readable polished transcript was available for export: {jobId}");
            }

            var content = TranscriptPolishingOutputNormalizer.Normalize(derivative.Content);
            if (!TranscriptPolishingOutputNormalizer.IsUsableDocument(content, out var reason))
            {
                throw new InvalidOperationException($"Readable polished transcript is not usable and must be regenerated: {reason}");
            }

            return new ExportSnapshot(
                jobId,
                title,
                pendingDraftCount,
                [],
                content);
        }

        var segments = new TranscriptReadRepository(paths)
            .ReadForJob(jobId)
            .Select(segment => new TranscriptExportSegment(
                segment.SegmentId,
                segment.StartSeconds,
                segment.EndSeconds,
                segment.Speaker,
                SelectExportText(segment, source),
                segment.RawText,
                string.IsNullOrWhiteSpace(segment.FinalText) ? string.Empty : segment.FinalText))
            .ToArray();
        return new ExportSnapshot(jobId, title, pendingDraftCount, segments);
    }

    private static string SelectExportText(TranscriptReadModel segment, TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => segment.RawText,
            TranscriptExportSource.Polished => segment.Text,
            TranscriptExportSource.ReadablePolished => segment.Text,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private static string GetSourceLogName(TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => "raw",
            TranscriptExportSource.Polished => "polished",
            TranscriptExportSource.ReadablePolished => "readable_polished",
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
        var renderSnapshot = ApplyFormatOptions(snapshot, format, options);
        if (format == TranscriptExportFormat.Docx)
        {
            WriteDocx(path, renderSnapshot, options);
            return;
        }

        if (format == TranscriptExportFormat.Xlsx)
        {
            WriteXlsx(path, renderSnapshot);
            return;
        }

        File.WriteAllText(path, Render(renderSnapshot, format, options), Encoding.UTF8);
    }

    private static ExportSnapshot ApplyFormatOptions(
        ExportSnapshot snapshot,
        TranscriptExportFormat format,
        TranscriptExportOptions options)
    {
        return options.MergeConsecutiveSpeakers && SupportsSpeakerMerging(format)
            ? snapshot with { Segments = MergeConsecutiveSpeakerSegments(snapshot.Segments) }
            : snapshot;
    }

    private static bool SupportsSpeakerMerging(TranscriptExportFormat format)
    {
        return format is TranscriptExportFormat.Text
            or TranscriptExportFormat.Markdown
            or TranscriptExportFormat.Docx
            or TranscriptExportFormat.Xlsx;
    }

    private static IReadOnlyList<TranscriptExportSegment> MergeConsecutiveSpeakerSegments(
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
            if (current is not null && CanMergeSpeakerSegments(current, segment))
            {
                current = MergeSpeakerSegments(current, segment);
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

    private static bool CanMergeSpeakerSegments(TranscriptExportSegment left, TranscriptExportSegment right)
    {
        return !string.IsNullOrWhiteSpace(left.Speaker)
            && string.Equals(left.Speaker, right.Speaker, StringComparison.Ordinal);
    }

    private static TranscriptExportSegment MergeSpeakerSegments(
        TranscriptExportSegment left,
        TranscriptExportSegment right)
    {
        return left with
        {
            SegmentId = $"{left.SegmentId}+{right.SegmentId}",
            EndSeconds = right.EndSeconds,
            Text = JoinExportText(left.Text, right.Text),
            RawText = JoinExportText(left.RawText, right.RawText),
            PolishedText = JoinExportText(left.PolishedText, right.PolishedText)
        };
    }

    private static string JoinExportText(string left, string right)
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

    private static string RenderText(ExportSnapshot snapshot, TranscriptExportOptions options)
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

    private static string RenderMarkdown(ExportSnapshot snapshot, TranscriptExportOptions options)
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

    private static string RenderJson(ExportSnapshot snapshot)
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

    private static string RenderSrt(ExportSnapshot snapshot)
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

    private static string RenderVtt(ExportSnapshot snapshot)
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
            TranscriptExportFormat.Xlsx => "xlsx",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    private static void WriteXlsx(string path, ExportSnapshot snapshot)
    {
        if (snapshot.DocumentContent is not null)
        {
            throw new InvalidOperationException("Readable polished transcript export does not support XLSX.");
        }

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
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """);
        WriteZipEntry(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """);
        WriteZipEntry(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """);
        WriteZipEntry(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Transcript" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """);
        WriteZipEntry(archive, "xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="2">
                <font><sz val="11"/><name val="Calibri"/></font>
                <font><b/><sz val="11"/><name val="Calibri"/></font>
              </fonts>
              <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="3">
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
                <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/>
                <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0" applyAlignment="1"><alignment wrapText="1" vertical="top"/></xf>
              </cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """);
        WriteZipEntry(archive, "xl/worksheets/sheet1.xml", RenderXlsxWorksheet(snapshot));
    }

    private static string RenderXlsxWorksheet(ExportSnapshot snapshot)
    {
        var builder = new StringBuilder()
            .AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""")
            .AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""")
            .AppendLine("""  <cols><col min="1" max="2" width="14" customWidth="1"/><col min="3" max="3" width="18" customWidth="1"/><col min="4" max="5" width="48" customWidth="1"/></cols>""")
            .AppendLine("  <sheetData>");

        AppendXlsxRow(builder, 1, ["開始時刻", "終了時刻", "話者", "素起こし", "整文"], styleId: 1);

        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            var segment = snapshot.Segments[i];
            AppendXlsxRow(
                builder,
                i + 2,
                [
                    TimestampFormatter.FormatDisplay(segment.StartSeconds),
                    TimestampFormatter.FormatDisplay(segment.EndSeconds),
                    segment.Speaker,
                    segment.RawText,
                    segment.PolishedText
                ],
                styleId: 2);
        }

        return builder
            .AppendLine("  </sheetData>")
            .AppendLine("</worksheet>")
            .ToString();
    }

    private static void AppendXlsxRow(StringBuilder builder, int rowIndex, IReadOnlyList<string> values, int styleId)
    {
        builder.Append("    <row r=\"").Append(rowIndex).Append("\">");
        for (var columnIndex = 0; columnIndex < values.Count; columnIndex++)
        {
            AppendInlineStringCell(builder, GetCellReference(columnIndex, rowIndex), values[columnIndex], styleId);
        }

        builder.AppendLine("</row>");
    }

    private static void AppendInlineStringCell(StringBuilder builder, string cellReference, string value, int styleId)
    {
        builder.Append("<c r=\"")
            .Append(cellReference)
            .Append("\" t=\"inlineStr\" s=\"")
            .Append(styleId)
            .Append("\"><is><t");

        if (RequiresXmlSpacePreserve(value))
        {
            builder.Append(" xml:space=\"preserve\"");
        }

        builder.Append('>')
            .Append(SecurityElement.Escape(value))
            .Append("</t></is></c>");
    }

    private static bool RequiresXmlSpacePreserve(string value)
    {
        return value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
    }

    private static string GetCellReference(int zeroBasedColumnIndex, int rowIndex)
    {
        var columnName = new StringBuilder();
        var index = zeroBasedColumnIndex;
        do
        {
            columnName.Insert(0, (char)('A' + index % 26));
            index = index / 26 - 1;
        }
        while (index >= 0);

        return columnName.Append(rowIndex).ToString();
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
            .AppendLine("<w:body>");

        if (snapshot.DocumentContent is { } documentContent)
        {
            AppendDocxTextParagraphs(builder, documentContent);
            return builder
                .AppendLine("<w:sectPr/>")
                .AppendLine("</w:body>")
                .AppendLine("</w:document>")
                .ToString();
        }

        builder.Append("<w:p><w:r><w:t>")
            .Append(SecurityElement.Escape(snapshot.Title))
            .AppendLine("</w:t></w:r></w:p>");

        if (snapshot.PendingDraftCount > 0)
        {
            builder.Append("<w:p><w:r><w:t>")
                .Append(SecurityElement.Escape($"{snapshot.PendingDraftCount}件の未処理の整文候補が残っています。"))
                .AppendLine("</w:t></w:r></w:p>");
        }

        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            if (options.MergeConsecutiveSpeakers && i > 0)
            {
                builder.AppendLine("<w:p><w:r><w:t></w:t></w:r></w:p>");
            }

            var segment = snapshot.Segments[i];
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

    private static void AppendDocxTextParagraphs(StringBuilder builder, string content)
    {
        var normalized = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        foreach (var line in normalized.Split('\n'))
        {
            builder.Append("<w:p><w:r><w:t");
            if (RequiresXmlSpacePreserve(line))
            {
                builder.Append(" xml:space=\"preserve\"");
            }

            builder.Append('>')
                .Append(SecurityElement.Escape(line))
                .AppendLine("</w:t></w:r></w:p>");
        }
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
        IReadOnlyList<TranscriptExportSegment> Segments,
        string? DocumentContent = null);
}
