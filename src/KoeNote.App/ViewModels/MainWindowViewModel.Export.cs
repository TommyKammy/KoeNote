using System.Diagnostics;
using System.IO;
using KoeNote.App.Services.Export;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task ExportSelectedJobAsync()
    {
        return ExportSelectedJobWithDialogAsync(null);
    }

    private Task ExportSelectedJobFormatAsync(TranscriptExportFormat format)
    {
        return ExportSelectedJobWithDialogAsync(format);
    }

    private Task ExportSelectedJobWithDialogAsync(TranscriptExportFormat? format)
    {
        if (SelectedJob is null)
        {
            return Task.CompletedTask;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export transcript",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = GetDefaultExportFileName(format ?? TranscriptExportFormat.Text),
            Filter = CreateExportFilter(format),
            FilterIndex = 1,
            DefaultExt = GetExtension(format ?? TranscriptExportFormat.Text)
        };

        dialog.InitialDirectory = GetOpenableExportFolder();

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        var selectedFormat = format ?? GetFormatFromFilterIndex(dialog.FilterIndex);
        ExportSelectedJobToFile(EnsureExtension(dialog.FileName, selectedFormat), selectedFormat);
        return Task.CompletedTask;
    }

    public void ExportSelectedJobToFile(string outputPath, TranscriptExportFormat format)
    {
        if (SelectedJob is null)
        {
            throw new InvalidOperationException("A job must be selected before export.");
        }

        try
        {
            var result = _transcriptExportService.ExportJobToFile(
                SelectedJob.JobId,
                outputPath,
                format,
                new TranscriptExportOptions(IncludeTimestamps: IncludeExportTimestamps));
            LastExportFolder = result.OutputDirectory;
            ExportWarning = result.HasUnresolvedDrafts
                ? $"{result.PendingDraftCount} unresolved correction draft(s) remain. Exported as confirmation output."
                : string.Empty;
            LatestLog = $"Exported transcript file: {string.Join(", ", result.FilePaths)}";
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ExportWarning = $"Export failed: {exception.Message}";
            LatestLog = ExportWarning;
            _jobLogRepository.AddEvent(SelectedJob.JobId, "export", "error", exception.Message);
            RefreshLogs();
        }
    }

    public void ExportSelectedJobToFolder(string outputDirectory, TranscriptExportFormat? format = null)
    {
        if (SelectedJob is null)
        {
            throw new InvalidOperationException("A job must be selected before export.");
        }

        try
        {
            var formats = format is null ? null : new[] { format.Value };
            var result = _transcriptExportService.ExportJob(
                SelectedJob.JobId,
                outputDirectory,
                formats,
                new TranscriptExportOptions(IncludeTimestamps: IncludeExportTimestamps));
            LastExportFolder = result.OutputDirectory;
            ExportWarning = result.HasUnresolvedDrafts
                ? $"{result.PendingDraftCount} unresolved correction draft(s) remain. Exported as confirmation output."
                : string.Empty;
            LatestLog = $"Exported {result.FilePaths.Count} transcript file(s): {string.Join(", ", result.FilePaths)}";
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ExportWarning = $"Export failed: {exception.Message}";
            LatestLog = ExportWarning;
            _jobLogRepository.AddEvent(SelectedJob.JobId, "export", "error", exception.Message);
            RefreshLogs();
        }
    }

    private Task OpenExportFolderAsync()
    {
        var exportFolder = GetOpenableExportFolder();
        Directory.CreateDirectory(exportFolder);

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{exportFolder}\"",
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private bool CanExportSelectedJob()
    {
        return SelectedJob is not null && Segments.Count > 0 && !IsRunInProgress;
    }

    private bool CanOpenExportFolder()
    {
        return true;
    }

    private string GetOpenableExportFolder()
    {
        if (!string.IsNullOrWhiteSpace(LastExportFolder) && Directory.Exists(LastExportFolder))
        {
            return LastExportFolder;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(documents, "KoeNote", "Exports");
    }

    private string GetDefaultExportFileName(TranscriptExportFormat format)
    {
        var baseName = SelectedJob is null
            ? "transcript"
            : Path.GetFileNameWithoutExtension(SelectedJob.FileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "transcript";
        }

        return $"{baseName}.{GetExtension(format)}";
    }

    private static string CreateExportFilter(TranscriptExportFormat? format)
    {
        return format is null
            ? "Text document (*.txt)|*.txt|Markdown (*.md)|*.md|JSON (*.json)|*.json|SRT subtitles (*.srt)|*.srt|WebVTT subtitles (*.vtt)|*.vtt|Word document (*.docx)|*.docx"
            : $"{GetFilterLabel(format.Value)} (*.{GetExtension(format.Value)})|*.{GetExtension(format.Value)}";
    }

    private static TranscriptExportFormat GetFormatFromFilterIndex(int filterIndex)
    {
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

    private void UpdateExportCommandStates()
    {
        if (ExportSelectedJobCommand is RelayCommand exportCommand)
        {
            exportCommand.RaiseCanExecuteChanged();
        }

        if (ExportTxtCommand is RelayCommand txtCommand)
        {
            txtCommand.RaiseCanExecuteChanged();
        }

        if (ExportJsonCommand is RelayCommand jsonCommand)
        {
            jsonCommand.RaiseCanExecuteChanged();
        }

        if (ExportSrtCommand is RelayCommand srtCommand)
        {
            srtCommand.RaiseCanExecuteChanged();
        }

        if (ExportDocxCommand is RelayCommand docxCommand)
        {
            docxCommand.RaiseCanExecuteChanged();
        }

        if (OpenExportFolderCommand is RelayCommand openCommand)
        {
            openCommand.RaiseCanExecuteChanged();
        }
    }
}
