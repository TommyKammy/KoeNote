using System.Diagnostics;
using System.IO;
using System.Windows.Input;
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
        return ExportSelectedJobWithDialogAsync(format, TranscriptExportSource.Polished);
    }

    private Task ExportSelectedJobFormatAsync(TranscriptExportFormat format, TranscriptExportSource source)
    {
        return ExportSelectedJobWithDialogAsync(format, source);
    }

    private Task ExportSelectedJobWithDialogAsync(TranscriptExportFormat? format)
    {
        return ExportSelectedJobWithDialogAsync(format, TranscriptExportSource.Polished);
    }

    private Task ExportSelectedJobWithDialogAsync(TranscriptExportFormat? format, TranscriptExportSource source)
    {
        if (SelectedJob is null)
        {
            return Task.CompletedTask;
        }

        var dialog = new SaveFileDialog
        {
            Title = $"{GetExportSourceDisplayName(source)}を出力",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = GetDefaultExportFileName(format ?? TranscriptExportFormat.Text, source),
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
        ExportSelectedJobToFile(EnsureExtension(dialog.FileName, selectedFormat), selectedFormat, source);
        return Task.CompletedTask;
    }

    public void ExportSelectedJobToFile(string outputPath, TranscriptExportFormat format)
    {
        ExportSelectedJobToFile(outputPath, format, TranscriptExportSource.Polished);
    }

    public void ExportSelectedJobToFile(string outputPath, TranscriptExportFormat format, TranscriptExportSource source)
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
                new TranscriptExportOptions(IncludeTimestamps: IncludeExportTimestamps, Source: source));
            LastExportFolder = result.OutputDirectory;
            ExportWarning = CreateExportWarning(result);
            LatestLog = $"{GetExportSourceDisplayName(source)}を出力しました: {string.Join(", ", result.FilePaths)}";
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ExportWarning = $"{GetExportSourceDisplayName(source)}の出力に失敗しました: {exception.Message}";
            LatestLog = ExportWarning;
            _jobLogRepository.AddEvent(SelectedJob.JobId, "export", "error", exception.Message);
            RefreshLogs();
        }
    }

    public void ExportSelectedJobToFolder(string outputDirectory, TranscriptExportFormat? format = null)
    {
        ExportSelectedJobToFolder(outputDirectory, format, TranscriptExportSource.Polished);
    }

    public void ExportSelectedJobToFolder(
        string outputDirectory,
        TranscriptExportFormat? format,
        TranscriptExportSource source)
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
                new TranscriptExportOptions(IncludeTimestamps: IncludeExportTimestamps, Source: source));
            LastExportFolder = result.OutputDirectory;
            ExportWarning = CreateExportWarning(result);
            LatestLog = $"{GetExportSourceDisplayName(source)}を{result.FilePaths.Count}件出力しました: {string.Join(", ", result.FilePaths)}";
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ExportWarning = $"{GetExportSourceDisplayName(source)}の出力に失敗しました: {exception.Message}";
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
        return GetDefaultExportFileName(format, TranscriptExportSource.Polished);
    }

    private string GetDefaultExportFileName(TranscriptExportFormat format, TranscriptExportSource source)
    {
        var baseName = SelectedJob is null
            ? "transcript"
            : Path.GetFileNameWithoutExtension(SelectedJob.FileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "transcript";
        }

        var suffix = source switch
        {
            TranscriptExportSource.Raw => ".raw",
            TranscriptExportSource.Polished => ".polished",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
        return $"{baseName}{suffix}.{GetExtension(format)}";
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

    private static string GetExportSourceDisplayName(TranscriptExportSource source)
    {
        return source switch
        {
            TranscriptExportSource.Raw => "素起こし",
            TranscriptExportSource.Polished => "整文",
            _ => throw new ArgumentOutOfRangeException(nameof(source), source, null)
        };
    }

    private static string CreateExportWarning(TranscriptExportResult result)
    {
        return result.HasUnresolvedDrafts
            ? $"未処理の整文候補が{result.PendingDraftCount}件残っています。確認用として出力しました。"
            : string.Empty;
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

        RaiseExportCommandState(ExportRawTxtCommand);
        RaiseExportCommandState(ExportRawMarkdownCommand);
        RaiseExportCommandState(ExportRawJsonCommand);
        RaiseExportCommandState(ExportRawSrtCommand);
        RaiseExportCommandState(ExportRawVttCommand);
        RaiseExportCommandState(ExportRawDocxCommand);
        RaiseExportCommandState(ExportPolishedTxtCommand);
        RaiseExportCommandState(ExportPolishedMarkdownCommand);
        RaiseExportCommandState(ExportPolishedDocxCommand);

        if (ExportSummaryMarkdownCommand is RelayCommand summaryCommand)
        {
            summaryCommand.RaiseCanExecuteChanged();
        }

        if (ExportSummaryTextCommand is RelayCommand summaryTextCommand)
        {
            summaryTextCommand.RaiseCanExecuteChanged();
        }

        if (OpenExportFolderCommand is RelayCommand openCommand)
        {
            openCommand.RaiseCanExecuteChanged();
        }
    }

    private static void RaiseExportCommandState(ICommand command)
    {
        if (command is RelayCommand relayCommand)
        {
            relayCommand.RaiseCanExecuteChanged();
        }
    }
}
