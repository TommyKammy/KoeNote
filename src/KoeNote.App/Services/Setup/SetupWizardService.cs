using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

public sealed class SetupWizardService
{
    private readonly SetupStateService _stateService;
    private readonly SetupModelSelectionService _selectionService;
    private readonly SetupModelInstallService _installService;
    private readonly SetupReadinessService _readinessService;

    public SetupWizardService(
        AppPaths paths,
        SetupStateService stateService,
        ToolStatusService toolStatusService,
        ModelCatalogService modelCatalogService,
        InstalledModelRepository installedModelRepository,
        ModelInstallService modelInstallService,
        ModelPackImportService modelPackImportService,
        ModelDownloadService modelDownloadService)
    {
        _stateService = stateService;
        _selectionService = new SetupModelSelectionService(paths, stateService, modelCatalogService);
        _installService = new SetupModelInstallService(
            stateService,
            _selectionService,
            installedModelRepository,
            modelInstallService,
            modelPackImportService,
            modelDownloadService);
        _readinessService = new SetupReadinessService(
            paths,
            stateService,
            toolStatusService,
            installedModelRepository);
    }

    public SetupState LoadState()
    {
        return _stateService.Load();
    }

    public IReadOnlyList<SetupEnvironmentCheck> GetEnvironmentChecks()
    {
        return _readinessService.GetEnvironmentChecks();
    }

    public IReadOnlyList<ModelCatalogEntry> GetSelectableModels(string role)
    {
        return _selectionService.GetSelectableModels(role);
    }

    public SetupState UseRecommendedSelections()
    {
        return _selectionService.UseRecommendedSelections();
    }

    public SetupState SelectSetupMode(string setupMode)
    {
        var normalized = string.IsNullOrWhiteSpace(setupMode) ? "guided" : setupMode.Trim();
        return _stateService.Save(_stateService.Load() with
        {
            CurrentStep = SetupStep.SetupMode,
            SetupMode = normalized
        });
    }

    public SetupState SelectModel(string role, string modelId)
    {
        return _selectionService.SelectModel(role, modelId);
    }

    public SetupState SetStorageRoot(string storageRoot)
    {
        return _selectionService.SetStorageRoot(storageRoot);
    }

    public SetupState AcceptLicenses()
    {
        return _stateService.Save(_stateService.Load() with
        {
            CurrentStep = SetupStep.License,
            LicenseAccepted = true
        });
    }

    public Task<SetupInstallResult> DownloadSelectedModelAsync(string role, CancellationToken cancellationToken = default)
    {
        return _installService.DownloadSelectedModelAsync(role, cancellationToken);
    }

    public Task<SetupInstallResult> DownloadSelectedModelAsync(
        string role,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return _installService.DownloadSelectedModelAsync(role, progress, cancellationToken);
    }

    public SetupInstallResult RegisterSelectedLocalModel(string role, string modelPath)
    {
        return _installService.RegisterSelectedLocalModel(role, modelPath);
    }

    public SetupInstallResult ImportOfflineModelPack(string modelPackPath)
    {
        return _installService.ImportOfflineModelPack(modelPackPath);
    }

    public IReadOnlyList<SetupModelAudit> GetSelectedModelAudit()
    {
        return _readinessService.GetSelectedModelAudit();
    }

    public SetupState MoveNext()
    {
        var state = _stateService.Load();
        var next = state.CurrentStep >= SetupStep.Complete ? SetupStep.Complete : state.CurrentStep + 1;
        return _stateService.Save(state with { CurrentStep = next });
    }

    public SetupState MoveBack()
    {
        var state = _stateService.Load();
        var previous = state.CurrentStep <= SetupStep.Welcome ? SetupStep.Welcome : state.CurrentStep - 1;
        return _stateService.Save(state with { CurrentStep = previous });
    }

    public SetupSmokeResult RunSmokeCheck()
    {
        return _readinessService.RunSmokeCheck();
    }

    public SetupState CompleteIfReady()
    {
        return _readinessService.CompleteIfReady();
    }

    public IReadOnlyList<SetupStepItem> BuildStepItems(SetupState state)
    {
        return SetupStepBuilder.Build(state);
    }
}
