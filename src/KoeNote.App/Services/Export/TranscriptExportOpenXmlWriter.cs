using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace KoeNote.App.Services.Export;

internal static class TranscriptExportOpenXmlWriter
{
    public static void WriteXlsx(string path, TranscriptExportSnapshot snapshot, TranscriptExportOptions options)
    {
        DeleteExisting(path);

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
        WriteZipEntry(archive, "xl/worksheets/sheet1.xml", RenderXlsxWorksheet(snapshot, options.Source));
    }

    public static void WriteDocx(string path, TranscriptExportSnapshot snapshot, TranscriptExportOptions options)
    {
        DeleteExisting(path);

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

    private static void DeleteExisting(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string RenderXlsxWorksheet(TranscriptExportSnapshot snapshot, TranscriptExportSource source)
    {
        var builder = new StringBuilder()
            .AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""")
            .AppendLine("""<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""")
            .AppendLine(GetXlsxColumns(source))
            .AppendLine("  <sheetData>");

        if (source == TranscriptExportSource.ReadablePolished)
        {
            AppendXlsxRow(builder, 1, ["読みやすく整文"], styleId: 1);
            AppendXlsxRow(builder, 2, [snapshot.DocumentContent ?? string.Empty], styleId: 2);
            return builder
                .AppendLine("  </sheetData>")
                .AppendLine("</worksheet>")
                .ToString();
        }

        AppendXlsxRow(builder, 1, GetXlsxHeaders(source), styleId: 1);

        for (var i = 0; i < snapshot.Segments.Count; i++)
        {
            AppendXlsxRow(
                builder,
                i + 2,
                GetXlsxSegmentValues(snapshot.Segments[i], source),
                styleId: 2);
        }

        return builder
            .AppendLine("  </sheetData>")
            .AppendLine("</worksheet>")
            .ToString();
    }

    private static string GetXlsxColumns(TranscriptExportSource source)
    {
        return source == TranscriptExportSource.ReadablePolished
            ? """  <cols><col min="1" max="1" width="120" customWidth="1"/></cols>"""
            : """  <cols><col min="1" max="2" width="14" customWidth="1"/><col min="3" max="3" width="18" customWidth="1"/><col min="4" max="4" width="64" customWidth="1"/></cols>""";
    }

    private static string[] GetXlsxHeaders(TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => ["開始時刻", "終了時刻", "話者", "素起こし"],
            TranscriptExportSource.Polished => ["開始時刻", "終了時刻", "話者", "整文"],
            TranscriptExportSource.ReadablePolished => ["読みやすく整文"],
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private static string[] GetXlsxSegmentValues(TranscriptExportSegment segment, TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw =>
            [
                TimestampFormatter.FormatDisplay(segment.StartSeconds),
                TimestampFormatter.FormatDisplay(segment.EndSeconds),
                segment.Speaker,
                segment.Text
            ],
            TranscriptExportSource.Polished =>
            [
                TimestampFormatter.FormatDisplay(segment.StartSeconds),
                TimestampFormatter.FormatDisplay(segment.EndSeconds),
                segment.Speaker,
                segment.Text
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
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

    private static string RenderDocxDocument(TranscriptExportSnapshot snapshot, TranscriptExportOptions options)
    {
        var builder = new StringBuilder()
            .AppendLine("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?>""")
            .AppendLine("""<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">""")
            .AppendLine("<w:body>");

        if (snapshot.DocumentContent is { } documentContent)
        {
            AppendDocxTextParagraphs(builder, documentContent);
            return CloseDocxDocument(builder);
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

        return CloseDocxDocument(builder);
    }

    private static string CloseDocxDocument(StringBuilder builder)
    {
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

    private static bool RequiresXmlSpacePreserve(string value)
    {
        return value.Length > 0 && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]));
    }

    private static void WriteZipEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
