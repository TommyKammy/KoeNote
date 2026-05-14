using System.Text;
using System.IO;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Transcript;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Export;

public sealed class TranscriptExportService(AppPaths paths)
{
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
            : GetDefaultFormats(exportOptions.Source);
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

    private TranscriptExportSnapshot LoadSnapshot(string jobId, TranscriptExportSource source)
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

            return new TranscriptExportSnapshot(
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
        return new TranscriptExportSnapshot(jobId, title, pendingDraftCount, segments);
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

    private static void WriteFormat(
        string path,
        TranscriptExportSnapshot snapshot,
        TranscriptExportFormat format,
        TranscriptExportOptions options)
    {
        var renderSnapshot = TranscriptExportSegmentMerger.ApplyFormatOptions(snapshot, format, options);
        if (format == TranscriptExportFormat.Docx)
        {
            TranscriptExportOpenXmlWriter.WriteDocx(path, renderSnapshot, options);
            return;
        }

        if (format == TranscriptExportFormat.Xlsx)
        {
            TranscriptExportOpenXmlWriter.WriteXlsx(path, renderSnapshot, options);
            return;
        }

        File.WriteAllText(
            path,
            TranscriptExportContentRenderer.Render(renderSnapshot, format, options),
            Encoding.UTF8);
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

    private static IReadOnlyCollection<TranscriptExportFormat> GetDefaultFormats(TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.ReadablePolished =>
            [
                TranscriptExportFormat.Text,
                TranscriptExportFormat.Markdown,
                TranscriptExportFormat.Docx,
                TranscriptExportFormat.Xlsx
            ],
            _ =>
            [
                TranscriptExportFormat.Text,
                TranscriptExportFormat.Markdown,
                TranscriptExportFormat.Json,
                TranscriptExportFormat.Srt,
                TranscriptExportFormat.Vtt
            ]
        };
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var sanitized = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "transcript" : sanitized;
    }
}
