using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Transcript;

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

        var selection = _transcriptExportDialogService.SelectExportFile(
            SelectedJob.FileName,
            GetOpenableExportFolder(),
            format,
            source);
        if (selection is null)
        {
            return Task.CompletedTask;
        }

        ExportSelectedJobToFile(selection.FilePath, selection.Format, selection.Source);
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
                new TranscriptExportOptions(
                    IncludeTimestamps: IncludeExportTimestamps,
                    Source: source,
                    MergeConsecutiveSpeakers: MergeConsecutiveSpeakersOnExport));
            LastExportFolder = result.OutputDirectory;
            ExportWarning = CreateExportWarning(result);
            LatestLog = $"{TranscriptExportDialogService.GetExportDisplayName(format, source)}を出力しました: {string.Join(", ", result.FilePaths)}";
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            ExportWarning = $"{TranscriptExportDialogService.GetExportDisplayName(format, source)}の出力に失敗しました: {exception.Message}";
            LatestLog = ExportWarning;
            _jobLogRepository.AddEvent(
                SelectedJob.JobId,
                "export",
                "error",
                JobLogDiagnostics.FormatException("transcript_export_failed", exception, outputPath));
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
                new TranscriptExportOptions(
                    IncludeTimestamps: IncludeExportTimestamps,
                    Source: source,
                    MergeConsecutiveSpeakers: MergeConsecutiveSpeakersOnExport));
            LastExportFolder = result.OutputDirectory;
            ExportWarning = CreateExportWarning(result);
            var exportName = format is { } selectedFormat
                ? TranscriptExportDialogService.GetExportDisplayName(selectedFormat, source)
                : TranscriptExportDialogService.GetSourceDisplayName(source);
            LatestLog = $"{exportName}を{result.FilePaths.Count}件出力しました: {string.Join(", ", result.FilePaths)}";
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            var exportName = format is { } selectedFormat
                ? TranscriptExportDialogService.GetExportDisplayName(selectedFormat, source)
                : TranscriptExportDialogService.GetSourceDisplayName(source);
            ExportWarning = $"{exportName}の出力に失敗しました: {exception.Message}";
            LatestLog = ExportWarning;
            _jobLogRepository.AddEvent(
                SelectedJob.JobId,
                "export",
                "error",
                JobLogDiagnostics.FormatException("transcript_export_failed", exception, outputDirectory));
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

    private bool CanExportReadablePolishing()
    {
        if (SelectedJob is null || IsRunInProgress)
        {
            return false;
        }

        var derivative = _transcriptDerivativeRepository.ReadLatestSuccessful(
            SelectedJob.JobId,
            TranscriptDerivativeKinds.Polished);
        return derivative is { Content.Length: > 0 } &&
            TranscriptPolishingOutputNormalizer.IsUsableDocument(derivative.Content, out _);
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

    private static string CreateExportWarning(TranscriptExportResult result)
    {
        return result.HasUnresolvedDrafts
            ? $"未処理のレビュー候補が{result.PendingDraftCount}件残っています。確認用として出力しました。"
            : string.Empty;
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
        RaiseExportCommandState(ExportRawXlsxCommand);
        RaiseExportCommandState(ExportRawJsonCommand);
        RaiseExportCommandState(ExportRawSrtCommand);
        RaiseExportCommandState(ExportRawVttCommand);
        RaiseExportCommandState(ExportRawDocxCommand);
        RaiseExportCommandState(ExportPolishedTxtCommand);
        RaiseExportCommandState(ExportPolishedMarkdownCommand);
        RaiseExportCommandState(ExportPolishedXlsxCommand);
        RaiseExportCommandState(ExportPolishedDocxCommand);
        RaiseExportCommandState(ExportReadablePolishedTxtCommand);
        RaiseExportCommandState(ExportReadablePolishedMarkdownCommand);
        RaiseExportCommandState(ExportReadablePolishedXlsxCommand);
        RaiseExportCommandState(ExportReadablePolishedDocxCommand);

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
