using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task ExportSelectedJobAsync()
    {
        if (SelectedJob is null)
        {
            return Task.CompletedTask;
        }

        var dialog = new OpenFolderDialog
        {
            Title = "Export transcript",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(LastExportFolder))
        {
            dialog.InitialDirectory = LastExportFolder;
        }

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        ExportSelectedJobToFolder(dialog.FolderName);
        return Task.CompletedTask;
    }

    public void ExportSelectedJobToFolder(string outputDirectory)
    {
        if (SelectedJob is null)
        {
            throw new InvalidOperationException("A job must be selected before export.");
        }

        try
        {
            var result = _transcriptExportService.ExportJob(SelectedJob.JobId, outputDirectory);
            LastExportFolder = result.OutputDirectory;
            ExportWarning = result.HasUnresolvedDrafts
                ? $"{result.PendingDraftCount} 件の未処理候補が残っています。確認用として出力しました。"
                : string.Empty;
            LatestLog = $"Exported {result.FilePaths.Count} transcript files: {result.OutputDirectory}";
            RefreshLogs();
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
        if (string.IsNullOrWhiteSpace(LastExportFolder) || !Directory.Exists(LastExportFolder))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{LastExportFolder}\"",
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
        return !string.IsNullOrWhiteSpace(LastExportFolder) && Directory.Exists(LastExportFolder);
    }

    private void UpdateExportCommandStates()
    {
        if (ExportSelectedJobCommand is RelayCommand exportCommand)
        {
            exportCommand.RaiseCanExecuteChanged();
        }

        if (OpenExportFolderCommand is RelayCommand openCommand)
        {
            openCommand.RaiseCanExecuteChanged();
        }
    }
}
