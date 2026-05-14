using System.IO;
using Microsoft.Win32;

namespace KoeNote.App.Services.Export;

public sealed class TranscriptExportDialogService
{
    public TranscriptExportDialogSelection? SelectExportFile(
        string jobFileName,
        string initialDirectory,
        TranscriptExportFormat? format,
        TranscriptExportSource source)
    {
        var defaultFormat = format ?? TranscriptExportFormat.Text;
        var dialog = new SaveFileDialog
        {
            Title = $"{GetExportDisplayName(defaultFormat, source)}を出力",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = CreateDefaultFileName(jobFileName, defaultFormat, source),
            Filter = CreateFilter(format, source),
            FilterIndex = 1,
            DefaultExt = GetExtension(defaultFormat),
            InitialDirectory = initialDirectory
        };

        if (dialog.ShowDialog() != true)
        {
            return null;
        }

        var selectedFormat = format ?? GetFormatFromFilterIndex(dialog.FilterIndex, source);
        return new TranscriptExportDialogSelection(
            EnsureExtension(dialog.FileName, selectedFormat),
            selectedFormat,
            source);
    }

    public static string GetSourceDisplayName(TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => "素起こし",
            TranscriptExportSource.Polished => "レビュー候補",
            TranscriptExportSource.ReadablePolished => "整文",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    public static string GetExportDisplayName(TranscriptExportFormat format, TranscriptExportSource source)
    {
        return format == TranscriptExportFormat.Xlsx
            ? $"{GetSourceDisplayName(source)} Excel"
            : GetSourceDisplayName(source);
    }

    private static string CreateDefaultFileName(
        string jobFileName,
        TranscriptExportFormat format,
        TranscriptExportSource source)
    {
        var baseName = Path.GetFileNameWithoutExtension(jobFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "transcript";
        }

        var suffix = source switch
        {
            TranscriptExportSource.Raw => ".raw",
            TranscriptExportSource.Polished => ".review-candidate",
            TranscriptExportSource.ReadablePolished => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
        return $"{baseName}{suffix}.{GetExtension(format)}";
    }

    private static string CreateFilter(TranscriptExportFormat? format, TranscriptExportSource source)
    {
        if (format is not null)
        {
            return $"{GetFilterLabel(format.Value)} (*.{GetExtension(format.Value)})|*.{GetExtension(format.Value)}";
        }

        return source == TranscriptExportSource.ReadablePolished
            ? "Text document (*.txt)|*.txt|Markdown (*.md)|*.md|Word document (*.docx)|*.docx|Excel workbook (*.xlsx)|*.xlsx"
            : "Text document (*.txt)|*.txt|Markdown (*.md)|*.md|JSON (*.json)|*.json|SRT subtitles (*.srt)|*.srt|WebVTT subtitles (*.vtt)|*.vtt|Word document (*.docx)|*.docx";
    }

    private static TranscriptExportFormat GetFormatFromFilterIndex(int filterIndex, TranscriptExportSource source)
    {
        if (source == TranscriptExportSource.ReadablePolished)
        {
            return filterIndex switch
            {
                2 => TranscriptExportFormat.Markdown,
                3 => TranscriptExportFormat.Docx,
                4 => TranscriptExportFormat.Xlsx,
                _ => TranscriptExportFormat.Text
            };
        }

        return filterIndex switch
        {
            2 => TranscriptExportFormat.Markdown,
            3 => TranscriptExportFormat.Json,
            4 => TranscriptExportFormat.Srt,
            5 => TranscriptExportFormat.Vtt,
            6 => TranscriptExportFormat.Docx,
            _ => TranscriptExportFormat.Text
        };
    }

    private static string GetFilterLabel(TranscriptExportFormat format)
    {
        return format switch
        {
            TranscriptExportFormat.Text => "Text document",
            TranscriptExportFormat.Markdown => "Markdown",
            TranscriptExportFormat.Json => "JSON",
            TranscriptExportFormat.Srt => "SRT subtitles",
            TranscriptExportFormat.Vtt => "WebVTT subtitles",
            TranscriptExportFormat.Docx => "Word document",
            TranscriptExportFormat.Xlsx => "Excel workbook",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
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

    private static string EnsureExtension(string path, TranscriptExportFormat format)
    {
        var extension = "." + GetExtension(format);
        return string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)
            ? path
            : Path.ChangeExtension(path, extension);
    }
}
