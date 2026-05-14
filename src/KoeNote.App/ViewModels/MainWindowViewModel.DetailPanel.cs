using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task OpenSettingsAsync()
    {
        OpenDetailPanel(0);
        LatestLog = "Settings opened. ASR engine, context, hotwords, storage path, and runtime readiness are available in the wide panel.";
        return Task.CompletedTask;
    }

    private Task OpenLogsAsync()
    {
        RefreshLogs();
        OpenDetailPanel(4);
        LatestLog = "Logs opened.";
        return Task.CompletedTask;
    }

    private Task ExportLogsAsync()
    {
        if (SelectedJob is null)
        {
            return Task.CompletedTask;
        }

        var dialog = new SaveFileDialog
        {
            Title = "診断パッケージを出力",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = CreateLogExportFileName(),
            Filter = "Diagnostic package (*.zip)|*.zip",
            FilterIndex = 1,
            DefaultExt = "zip",
            InitialDirectory = GetOpenableExportFolder()
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        var outputPath = string.Equals(Path.GetExtension(dialog.FileName), ".zip", StringComparison.OrdinalIgnoreCase)
            ? dialog.FileName
            : Path.ChangeExtension(dialog.FileName, "zip");

        try
        {
            _jobLogExportService.ExportDiagnosticPackage(SelectedJob, outputPath, SelectedDiagnosticLogScopeValue);
            LastExportFolder = Path.GetDirectoryName(outputPath) ?? LastExportFolder;
            LatestLog = $"診断パッケージを出力しました: {outputPath}";
            RefreshLogs();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            LatestLog = $"ログ出力に失敗しました: {exception.Message}";
            RefreshLogs();
        }

        return Task.CompletedTask;
    }

    private bool CanExportLogs()
    {
        return SelectedJob is not null;
    }

    private Task OpenLogFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(Paths.Logs);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{Paths.Logs}\"",
                UseShellExecute = true
            });
            LatestLog = $"ログフォルダを開きました: {Paths.Logs}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            LatestLog = $"ログフォルダを開けませんでした: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    private void RefreshLogCommandStates()
    {
        if (ExportLogsCommand is RelayCommand exportLogsCommand)
        {
            exportLogsCommand.RaiseCanExecuteChanged();
        }
    }

    private static string CreateLogExportFileName()
    {
        return $"koenote-diagnostic-package-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    private Task OpenCleanupToolAsync()
    {
        var cleanupToolPath = GetCleanupToolPath();
        if (cleanupToolPath is null)
        {
            LatestLog = "クリーンアップツールが見つかりません。インストール済み環境では KoeNoteCleanup.exe が同じフォルダーに配置されます。";
            return Task.CompletedTask;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = cleanupToolPath,
                WorkingDirectory = Path.GetDirectoryName(cleanupToolPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            LatestLog = $"クリーンアップツールを起動しました: {cleanupToolPath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            LatestLog = $"クリーンアップツールを起動できませんでした: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    private bool CanOpenCleanupTool()
    {
        return !IsRunInProgress && GetCleanupToolPath() is not null;
    }

    private static string? GetCleanupToolPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "KoeNoteCleanup.exe");
        return File.Exists(path) ? path : null;
    }

    private Task OpenSetupAsync()
    {
        RefreshModelCatalog();
        RefreshSetupWizard();
        IsSetupWizardModalOpen = true;
        LatestLog = $"{SetupPlaceholderText}{Environment.NewLine}Setup step: {SetupCurrentStep}. Model catalog: {ModelCatalogEntries.Count} entries.";
        return Task.CompletedTask;
    }

    private Task CloseSetupWizardModalAsync()
    {
        IsSetupWizardModalOpen = false;
        LatestLog = IsSetupComplete
            ? "Setup wizard closed."
            : "Setup wizard closed for now. Run remains disabled until setup is completed.";
        return Task.CompletedTask;
    }

    private Task OpenSelectedDetailPanelAsync()
    {
        OpenDetailPanel(SelectedLogPanelTabIndex);
        return Task.CompletedTask;
    }

    private bool CanOpenSelectedDetailPanel()
    {
        return true;
    }

    private Task CloseDetailPanelAsync()
    {
        IsDetailPanelOpen = false;
        return Task.CompletedTask;
    }

    private void OpenDetailPanel(int logPanelTabIndex)
    {
        SelectedLogPanelTabIndex = Math.Clamp(logPanelTabIndex, 0, 4);
        SelectedDetailPanelTabIndex = SelectedLogPanelTabIndex;
        OnPropertyChanged(nameof(DetailPanelTitle));
        IsDetailPanelOpen = true;
    }
}
