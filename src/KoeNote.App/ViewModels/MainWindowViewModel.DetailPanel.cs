using System.ComponentModel;
using System.Diagnostics;
using System.IO;

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
        OpenDetailPanel(3);
        LatestLog = "Logs opened.";
        return Task.CompletedTask;
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
        SelectedLogPanelTabIndex = Math.Clamp(logPanelTabIndex, 0, 3);
        SelectedDetailPanelTabIndex = SelectedLogPanelTabIndex;
        OnPropertyChanged(nameof(DetailPanelTitle));
        IsDetailPanelOpen = true;
    }
}
