using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using System.Net.Http;

namespace KoeNote.App.Services.Setup;

public sealed class SetupWizardService
{
    private readonly SetupStateService _stateService;
    private readonly SetupModelSelectionService _selectionService;
    private readonly SetupModelInstallService _installService;
    private readonly SetupReadinessService _readinessService;
    private readonly SetupPresetRecommendationService _presetRecommendationService;
    private readonly FasterWhisperRuntimeService _fasterWhisperRuntimeService;
    private readonly AsrCudaRuntimeService _asrCudaRuntimeService;
    private readonly DiarizationRuntimeService _diarizationRuntimeService;
    private readonly TernaryReviewRuntimeService _ternaryReviewRuntimeService;
    private readonly CudaReviewRuntimeService _cudaReviewRuntimeService;
    private SetupPresetRecommendation? _presetRecommendation;
    private bool _automaticPresetRecommendationApplied;

    public SetupWizardService(
        AppPaths paths,
        SetupStateService stateService,
        ToolStatusService toolStatusService,
        ModelCatalogService modelCatalogService,
        InstalledModelRepository installedModelRepository,
        ModelInstallService modelInstallService,
        ModelPackImportService modelPackImportService,
        ModelDownloadService modelDownloadService,
        FasterWhisperRuntimeService fasterWhisperRuntimeService,
        DiarizationRuntimeService diarizationRuntimeService,
        ISetupHostResourceProbe? hostResourceProbe = null,
        AsrCudaRuntimeService? asrCudaRuntimeService = null,
        TernaryReviewRuntimeService? ternaryReviewRuntimeService = null,
        CudaReviewRuntimeService? cudaReviewRuntimeService = null)
    {
        _fasterWhisperRuntimeService = fasterWhisperRuntimeService;
        _asrCudaRuntimeService = asrCudaRuntimeService ?? new AsrCudaRuntimeService(paths, new HttpClient());
        _diarizationRuntimeService = diarizationRuntimeService;
        _ternaryReviewRuntimeService = ternaryReviewRuntimeService ?? new TernaryReviewRuntimeService(paths, new HttpClient());
        _cudaReviewRuntimeService = cudaReviewRuntimeService ?? new CudaReviewRuntimeService(paths, new HttpClient());
        _stateService = stateService;
        _selectionService = new SetupModelSelectionService(paths, stateService, modelCatalogService);
        var resourceProbe = hostResourceProbe ?? new WindowsSetupHostResourceProbe();
        _presetRecommendationService = new SetupPresetRecommendationService(
            modelCatalogService,
            resourceProbe);
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
            modelCatalogService,
            installedModelRepository,
            resourceProbe);
    }

    public SetupState LoadState()
    {
        var state = _selectionService.RepairUnsupportedSelections(_stateService.Load());
        if (!state.IsCompleted && state.CurrentStep is SetupStep.Welcome or SetupStep.EnvironmentCheck)
        {
            state = _stateService.Save(state with { CurrentStep = SetupStep.SetupMode });
        }

        if (state.IsCompleted && _readinessService.IsSelectedTernaryReviewRuntimeMissing(state))
        {
            return _readinessService.CompleteIfReady();
        }

        return !state.IsCompleted && _readinessService.IsCompleteStateReady(state)
            ? _readinessService.CompleteIfReady()
            : state;
    }

    public IReadOnlyList<SetupEnvironmentCheck> GetEnvironmentChecks()
    {
        return _readinessService.GetEnvironmentChecks();
    }

    public IReadOnlyList<ModelCatalogEntry> GetSelectableModels(string role)
    {
        return _selectionService.GetSelectableModels(role);
    }

    public IReadOnlyList<ModelQualityPreset> GetModelPresets()
    {
        return _selectionService.GetModelPresets();
    }

    public SetupPresetRecommendation GetPresetRecommendation()
    {
        _presetRecommendation ??= _presetRecommendationService.GetRecommendation();
        return _presetRecommendation;
    }

    public SetupState ApplyAutomaticModelPresetRecommendation()
    {
        var state = _stateService.Load();
        if (_automaticPresetRecommendationApplied)
        {
            return state;
        }

        _automaticPresetRecommendationApplied = true;
        if (state.IsCompleted ||
            state.CurrentStep is not (SetupStep.Welcome or SetupStep.SetupMode) ||
            !string.Equals(state.SelectedModelPresetId, "recommended", StringComparison.OrdinalIgnoreCase))
        {
            return state;
        }

        var recommendation = GetPresetRecommendation();
        if (string.Equals(state.SelectedModelPresetId, recommendation.PresetId, StringComparison.OrdinalIgnoreCase))
        {
            return state;
        }

        if (!GetModelPresets().Any(preset =>
                preset.PresetId.Equals(recommendation.PresetId, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        return _selectionService.SelectPreset(recommendation.PresetId, advanceToModelStep: false);
    }

    public SetupState UseRecommendedSelections()
    {
        var state = _selectionService.UseRecommendedSelections();
        return _stateService.Save(state with
        {
            CurrentStep = SetupStep.InstallPlan,
            LicenseAccepted = true
        });
    }

    public SetupState SelectModelPreset(string presetId)
    {
        var state = _selectionService.SelectPreset(presetId, advanceToModelStep: false);
        return state.CurrentStep is SetupStep.Welcome or SetupStep.EnvironmentCheck
            ? _stateService.Save(state with { CurrentStep = SetupStep.SetupMode })
            : state;
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
            CurrentStep = SetupStep.InstallPlan,
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

    public Task<SetupInstallResult> InstallSelectedPresetModelsAsync(
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        return _installService.DownloadSelectedPresetModelsAsync(progress, cancellationToken);
    }

    public Task<FasterWhisperRuntimeInstallResult> InstallFasterWhisperRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return _fasterWhisperRuntimeService.InstallAsync(cancellationToken);
    }

    public Task<FasterWhisperRuntimePreflightStatus> CheckFasterWhisperRuntimeInstallPreflightAsync(CancellationToken cancellationToken = default)
    {
        return _fasterWhisperRuntimeService.CheckInstallPreflightAsync(cancellationToken);
    }

    public Task<FasterWhisperRuntimeStatus> CheckFasterWhisperRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return _fasterWhisperRuntimeService.CheckAsync(cancellationToken);
    }

    public Task<AsrCudaRuntimeInstallResult> InstallAsrCudaRuntimeAsync(
        CancellationToken cancellationToken = default,
        IProgress<RuntimeInstallProgress>? progress = null)
    {
        return _asrCudaRuntimeService.InstallAsync(cancellationToken, progress);
    }

    public Task<DiarizationRuntimeInstallResult> InstallDiarizationRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return _diarizationRuntimeService.InstallAsync(cancellationToken);
    }

    public Task<DiarizationRuntimePreflightStatus> CheckDiarizationRuntimeInstallPreflightAsync(CancellationToken cancellationToken = default)
    {
        return _diarizationRuntimeService.CheckInstallPreflightAsync(cancellationToken);
    }

    public Task<DiarizationRuntimeStatus> CheckDiarizationRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return _diarizationRuntimeService.CheckAsync(cancellationToken);
    }

    public Task<TernaryReviewRuntimeInstallResult> InstallTernaryReviewRuntimeAsync(CancellationToken cancellationToken = default)
    {
        return _ternaryReviewRuntimeService.InstallAsync(cancellationToken);
    }

    public Task<CudaReviewRuntimeInstallResult> InstallCudaReviewRuntimeAsync(
        CancellationToken cancellationToken = default,
        IProgress<RuntimeInstallProgress>? progress = null)
    {
        return _cudaReviewRuntimeService.InstallAsync(cancellationToken, progress);
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

    public IReadOnlyList<SetupExistingDataItem> GetExistingDataSummary()
    {
        return _readinessService.GetExistingDataSummary();
    }

    public SetupState MoveNext()
    {
        var state = _stateService.Load();
        if (SetupStepFlow.HasReached(state.CurrentStep, SetupStep.License) && !state.LicenseAccepted)
        {
            return _stateService.Save(state with { CurrentStep = SetupStep.License });
        }

        return _stateService.Save(state with { CurrentStep = SetupStepFlow.GetNext(state.CurrentStep) });
    }

    public SetupState MoveNextGuided()
    {
        var state = _stateService.Load();
        return state.CurrentStep switch
        {
            SetupStep.Welcome or SetupStep.EnvironmentCheck => _stateService.Save(state with { CurrentStep = SetupStep.SetupMode }),
            SetupStep.SetupMode => ApplyGuidedPresetAndMoveToInstallPlan(state),
            SetupStep.AsrModel or SetupStep.ReviewModel or SetupStep.Storage => MoveToInstallPlanWithAcceptedLicenses(state),
            SetupStep.License => AcceptLicenses(),
            SetupStep.InstallPlan when !state.LicenseAccepted => MoveToInstallPlanWithAcceptedLicenses(state),
            SetupStep.InstallPlan => state,
            _ => MoveNext()
        };
    }

    public SetupState MoveToLicense()
    {
        return _stateService.Save(_stateService.Load() with { CurrentStep = SetupStep.License });
    }

    public SetupState MoveToInstallPlan()
    {
        var state = _stateService.Load();
        return MoveToInstallPlanWithAcceptedLicenses(state);
    }

    public SetupState MoveToPresetSelection()
    {
        var state = _stateService.Load();
        return _stateService.Save(state with
        {
            IsCompleted = false,
            LastSmokeSucceeded = false,
            CurrentStep = SetupStep.SetupMode
        });
    }

    public SetupState MoveBack()
    {
        var state = _stateService.Load();
        return _stateService.Save(state with { CurrentStep = SetupStepFlow.GetPrevious(state.CurrentStep) });
    }

    private SetupState ApplyGuidedPresetAndMoveToInstallPlan(SetupState state)
    {
        if (string.Equals(state.SetupMode, "custom", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state.SelectedModelPresetId))
        {
            return MoveToInstallPlanWithAcceptedLicenses(state);
        }

        var presetId = state.SelectedModelPresetId.Equals("recommended", StringComparison.OrdinalIgnoreCase)
            ? GetPresetRecommendation().PresetId
            : state.SelectedModelPresetId;
        if (!GetModelPresets().Any(preset =>
                preset.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase)))
        {
            return MoveToInstallPlanWithAcceptedLicenses(state);
        }

        var selected = _selectionService.SelectPreset(presetId, advanceToModelStep: false);
        return MoveToInstallPlanWithAcceptedLicenses(selected);
    }

    private SetupState MoveToInstallPlanWithAcceptedLicenses(SetupState state)
    {
        return _stateService.Save(state with
        {
            CurrentStep = SetupStep.InstallPlan,
            LicenseAccepted = true
        });
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
        return SetupStepBuilder.Build(state, _readinessService.GetStepReadiness(state));
    }
}
