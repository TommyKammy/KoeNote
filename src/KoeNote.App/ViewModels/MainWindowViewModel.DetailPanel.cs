namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task OpenSettingsAsync()
    {
        OpenDetailPanel(1);
        LatestLog = "Settings opened. ASR engine, context, hotwords, storage path, and runtime readiness are available in the wide panel.";
        return Task.CompletedTask;
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
        OpenDetailPanel(SelectedLogPanelTabIndex == 0 ? 2 : SelectedLogPanelTabIndex);
        return Task.CompletedTask;
    }

    private bool CanOpenSelectedDetailPanel()
    {
        return SelectedLogPanelTabIndex > 0;
    }

    private Task CloseDetailPanelAsync()
    {
        IsDetailPanelOpen = false;
        return Task.CompletedTask;
    }

    private void OpenDetailPanel(int logPanelTabIndex)
    {
        SelectedLogPanelTabIndex = Math.Clamp(logPanelTabIndex, 1, 3);
        SelectedDetailPanelTabIndex = SelectedLogPanelTabIndex - 1;
        OnPropertyChanged(nameof(DetailPanelTitle));
        IsDetailPanelOpen = true;
    }
}
