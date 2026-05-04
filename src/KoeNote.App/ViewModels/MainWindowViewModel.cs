using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.SystemStatus;

namespace KoeNote.App.ViewModels;

public sealed record AsrEngineOption(string EngineId, string DisplayName);

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly JobRepository _jobRepository;
    private readonly JobLogRepository _jobLogRepository;
    private readonly StageProgressRepository _stageProgressRepository;
    private readonly AsrSettingsRepository _asrSettingsRepository;
    private readonly AudioPreprocessWorker _audioPreprocessWorker;
    private readonly TranscriptSegmentRepository _transcriptSegmentRepository;
    private readonly AsrWorker _asrWorker;
    private readonly AsrEngineRegistry _asrEngineRegistry;
    private readonly ReviewWorker _reviewWorker;
    private readonly JobRunCoordinator _jobRunCoordinator;
    private readonly CorrectionDraftRepository _correctionDraftRepository;
    private readonly ReviewOperationService _reviewOperationService;
    private readonly TranscriptEditService _transcriptEditService;
    private readonly CorrectionMemoryService _correctionMemoryService;
    private readonly TranscriptExportService _transcriptExportService;
    private readonly ModelCatalogService _modelCatalogService;
    private readonly InstalledModelRepository _installedModelRepository;
    private readonly ModelInstallService _modelInstallService;
    private readonly ModelLicenseViewer _modelLicenseViewer;
    private readonly SetupStateService _setupStateService;
    private readonly SetupWizardService _setupWizardService;
    private readonly TextDiffService _textDiffService = new();
    private readonly StatusBarInfoService _statusBarInfoService;
    private readonly DispatcherTimer _statusRefreshTimer;
    private StatusBarInfo _statusBarInfo;
    private bool _isStatusRefreshInProgress;
    private JobSummary? _selectedJob;
    private TranscriptSegmentPreview? _selectedSegment;
    private CorrectionDraft? _selectedCorrectionDraft;
    private ModelCatalogEntry? _selectedModelCatalogEntry;
    private ModelCatalogEntry? _selectedSetupAsrModel;
    private ModelCatalogEntry? _selectedSetupReviewModel;
    private SetupState _setupState;
    private string _setupLocalModelPath = string.Empty;
    private string _setupOfflineModelPackPath = string.Empty;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _asrSettingsSaveDebounce;
    private bool _isReviewOperationInProgress;
    private bool _rememberCorrection = true;
    private string _exportWarning = string.Empty;
    private string _lastExportFolder = string.Empty;
    private string _latestLog;
    private string _jobSearchText = string.Empty;
    private string _segmentSearchText = string.Empty;
    private string _selectedSpeakerFilter = "全話者";
    private string _asrContextText = string.Empty;
    private string _asrHotwordsText = string.Empty;
    private string _selectedAsrEngineId = VibeVoiceCrispAsrEngine.Id;
    private string _selectedSegmentEditText = string.Empty;
    private string _selectedSpeakerAlias = string.Empty;
    private bool _isRunInProgress;
    private string _reviewIssueType = "意味不明語の疑い";
    private string _originalText = "この仕様はサーバーのミギワで処理します。";
    private string _suggestedText = "この仕様はサーバーの右側で処理します。";
    private string _reviewReason = "文脈上「ミギワ」が不自然で、音の近い語として「右側」が候補になる。";
    private double _confidence = 0.62;

    public MainWindowViewModel()
        : this(new AppPaths())
    {
    }

    public MainWindowViewModel(AppPaths paths)
    {
        Paths = paths;
        Paths.EnsureCreated();

        var database = new DatabaseInitializer(Paths);
        database.EnsureCreated();

        _jobRepository = new JobRepository(Paths);
        _stageProgressRepository = new StageProgressRepository(Paths);
        _jobLogRepository = new JobLogRepository(Paths);
        _asrSettingsRepository = new AsrSettingsRepository(Paths);
        _correctionDraftRepository = new CorrectionDraftRepository(Paths);
        _reviewOperationService = new ReviewOperationService(Paths);
        _transcriptEditService = new TranscriptEditService(Paths);
        _correctionMemoryService = new CorrectionMemoryService(Paths);
        _transcriptExportService = new TranscriptExportService(Paths);
        _modelCatalogService = new ModelCatalogService(Paths);
        _installedModelRepository = new InstalledModelRepository(Paths);
        var modelVerificationService = new ModelVerificationService();
        _modelInstallService = new ModelInstallService(Paths, _installedModelRepository, modelVerificationService);
        _modelLicenseViewer = new ModelLicenseViewer(_modelCatalogService);
        var modelPackImportService = new ModelPackImportService(Paths, _modelCatalogService, _modelInstallService);
        var modelDownloadService = new ModelDownloadService(
            new HttpClient(),
            new ModelDownloadJobRepository(Paths),
            modelVerificationService,
            _modelInstallService);
        _setupStateService = new SetupStateService(Paths);
        _setupWizardService = new SetupWizardService(
            Paths,
            _setupStateService,
            new ToolStatusService(Paths),
            _modelCatalogService,
            _installedModelRepository,
            _modelInstallService,
            modelPackImportService,
            modelDownloadService);
        _setupState = _setupWizardService.LoadState();
        _transcriptSegmentRepository = new TranscriptSegmentRepository(Paths);
        var processRunner = new ExternalProcessRunner();
        _audioPreprocessWorker = new AudioPreprocessWorker(processRunner, _stageProgressRepository, _jobLogRepository);
        _asrWorker = new AsrWorker(
            processRunner,
            new AsrCommandBuilder(),
            new AsrJsonNormalizer(),
            new AsrResultStore(),
            _transcriptSegmentRepository);
        _reviewWorker = new ReviewWorker(
            processRunner,
            new ReviewCommandBuilder(),
            new ReviewPromptBuilder(),
            new ReviewJsonNormalizer(),
            new ReviewResultStore(),
            new CorrectionDraftRepository(Paths),
            _correctionMemoryService);
        _asrEngineRegistry = new AsrEngineRegistry([
            new VibeVoiceCrispAsrEngine(_asrWorker, new AsrRunRepository(Paths)),
            new ScriptedJsonAsrEngine(
                "faster-whisper-large-v3-turbo",
                "faster-whisper large-v3-turbo",
                "faster-whisper",
                processRunner,
                new AsrJsonNormalizer(),
                new AsrResultStore(),
                _transcriptSegmentRepository,
                new AsrRunRepository(Paths)),
            new ScriptedJsonAsrEngine(
                "reazonspeech-k2-v3",
                "ReazonSpeech v3 k2",
                "reazonspeech-k2",
                processRunner,
                new AsrJsonNormalizer(),
                new AsrResultStore(),
                _transcriptSegmentRepository,
                new AsrRunRepository(Paths))
        ]);
        _jobRunCoordinator = new JobRunCoordinator(
            Paths,
            _jobRepository,
            _stageProgressRepository,
            _jobLogRepository,
            _audioPreprocessWorker,
            _asrEngineRegistry,
            _installedModelRepository,
            _reviewWorker,
            _correctionMemoryService);
        _statusBarInfoService = new StatusBarInfoService(Paths);
        _statusBarInfo = _statusBarInfoService.GetStatusBarInfo();
        _statusRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusRefreshTimer.Tick += async (_, _) => await RefreshStatusBarInfoAsync();
        _statusRefreshTimer.Start();

        var toolStatus = new ToolStatusService(Paths);
        foreach (var item in toolStatus.GetStatusItems())
        {
            EnvironmentStatus.Add(item);
        }

        foreach (var stageStatus in CreateStageStatuses())
        {
            StageStatuses.Add(stageStatus);
        }

        _latestLog = $"Initialized AppData at {Paths.Root}";
        foreach (var engine in _asrEngineRegistry.Engines)
        {
            AvailableAsrEngines.Add(new AsrEngineOption(engine.EngineId, engine.DisplayName));
        }

        var asrSettings = _asrSettingsRepository.Load();
        _asrContextText = asrSettings.ContextText;
        _asrHotwordsText = asrSettings.HotwordsText;
        _selectedAsrEngineId = _asrEngineRegistry.Contains(asrSettings.EngineId)
            ? asrSettings.EngineId
            : VibeVoiceCrispAsrEngine.Id;
        FilteredJobs = CollectionViewSource.GetDefaultView(Jobs);
        FilteredJobs.Filter = FilterJob;
        FilteredSegments = CollectionViewSource.GetDefaultView(Segments);
        FilteredSegments.Filter = FilterSegment;
        LoadJobs();
        RefreshLogs();

        if (Segments.Count == 0)
        {
        Segments.Add(new TranscriptSegmentPreview(
            "00:00:00.000",
            "00:00:05.810",
            "Speaker_0",
            "今日は会議の議事録を作成するために音声認識をテストしています。",
            "候補なし"));

        Segments.Add(new TranscriptSegmentPreview(
            "00:03:21.400",
            "00:03:27.800",
            "Speaker_1",
            "この仕様はサーバーのミギワで処理します。",
            "推敲候補あり"));

        }

        AddAudioCommand = new RelayCommand(AddAudioAsync);
        DeleteJobCommand = new RelayCommand<JobSummary>(DeleteJobAsync, job => job is not null && !IsRunInProgress);
        ClearAllJobsCommand = new RelayCommand(ClearAllJobsAsync, () => Jobs.Count > 0 && !IsRunInProgress);
        RunSelectedJobCommand = new RelayCommand(RunSelectedJobAsync, () => CanRunSelectedJob);
        CancelCommand = new RelayCommand(CancelRunAsync, () => IsRunInProgress);
        OpenSetupCommand = new RelayCommand(OpenSetupAsync);
        SetupBackCommand = new RelayCommand(SetupBackAsync);
        SetupNextCommand = new RelayCommand(SetupNextAsync);
        SetupUseRecommendedCommand = new RelayCommand(SetupUseRecommendedAsync);
        SetupAcceptLicensesCommand = new RelayCommand(SetupAcceptLicensesAsync);
        SetupDownloadAsrCommand = new RelayCommand(SetupDownloadAsrAsync);
        SetupDownloadReviewCommand = new RelayCommand(SetupDownloadReviewAsync);
        SetupRegisterLocalAsrCommand = new RelayCommand(SetupRegisterLocalAsrAsync);
        SetupRegisterLocalReviewCommand = new RelayCommand(SetupRegisterLocalReviewAsync);
        SetupImportOfflinePackCommand = new RelayCommand(SetupImportOfflinePackAsync);
        SetupRunSmokeCommand = new RelayCommand(SetupRunSmokeAsync);
        SetupCompleteCommand = new RelayCommand(SetupCompleteAsync);
        ShowModelCatalogCommand = new RelayCommand(ShowModelCatalogAsync);
        RegisterPreinstalledModelsCommand = new RelayCommand(RegisterPreinstalledModelsAsync);
        UseSelectedModelCommand = new RelayCommand(UseSelectedModelAsync, () => SelectedModelCatalogEntry is not null);
        ShowSelectedModelLicenseCommand = new RelayCommand(ShowSelectedModelLicenseAsync, () => SelectedModelCatalogEntry is not null);
        ForgetSelectedModelCommand = new RelayCommand(ForgetSelectedModelAsync, () => SelectedModelCatalogEntry?.IsInstalled == true);
        AcceptDraftCommand = new RelayCommand(AcceptSelectedDraftAsync, CanOperateOnSelectedDraft);
        RejectDraftCommand = new RelayCommand(RejectSelectedDraftAsync, CanOperateOnSelectedDraft);
        ApplyManualEditCommand = new RelayCommand(ApplyManualEditAsync, CanOperateOnSelectedDraft);
        SelectPreviousDraftCommand = new RelayCommand(SelectPreviousDraftAsync, CanSelectPreviousDraft);
        SelectNextDraftCommand = new RelayCommand(SelectNextDraftAsync, CanSelectNextDraft);
        SaveSegmentEditCommand = new RelayCommand(SaveSegmentEditAsync, CanEditSelectedSegment);
        SaveSpeakerAliasCommand = new RelayCommand(SaveSpeakerAliasAsync, CanEditSelectedSpeaker);
        UndoLastOperationCommand = new RelayCommand(UndoLastOperationAsync);
        ExportSelectedJobCommand = new RelayCommand(ExportSelectedJobAsync, CanExportSelectedJob);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolderAsync, CanOpenExportFolder);

        RefreshModelCatalog();
        RefreshSetupWizard();
        RefreshSpeakerFilters();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppPaths Paths { get; }

    public string AppMode => "Offline";

    public string GpuSummary => EnvironmentStatus.FirstOrDefault(item => item.Name == "nvidia-smi")?.Detail ?? "Unknown";

    public string AsrModel => AvailableAsrEngines
        .FirstOrDefault(engine => string.Equals(engine.EngineId, SelectedAsrEngineId, StringComparison.OrdinalIgnoreCase))
        ?.DisplayName ?? "VibeVoice ASR Q4";

    public string ReviewModel => "llm-jp Q4_K_M";

    public string StoragePath => Paths.Root;

    public string DiskFreeSummary => _statusBarInfo.DiskFreeSummary;

    public string MemorySummary => _statusBarInfo.MemorySummary;

    public string CpuSummary => _statusBarInfo.CpuSummary;

    public string GpuUsageSummary => _statusBarInfo.GpuUsageSummary;

    public string FirstRunSummary
    {
        get
        {
            var missingCount = EnvironmentStatus.Count(static item => !item.IsOk);
            return missingCount == 0
                ? "初回チェック OK"
                : $"初回チェック: {missingCount} 件の確認が必要";
        }
    }

    public string FirstRunDetail
    {
        get
        {
            var missingItems = EnvironmentStatus.Where(static item => !item.IsOk).ToArray();
            return missingItems.Length == 0
                ? "必要なランタイム、ツール、モデルを確認済みです。"
                : string.Join(Environment.NewLine, missingItems.Select(static item => $"{item.Name}: {item.Detail}"))
                    + Environment.NewLine
                    + "セットアップを開く / モデル導入へ から Phase 11 Model Catalog / Phase 12 Setup Wizard に進む予定です。";
        }
    }

    public string SetupPlaceholderText =>
        "Phase 11 の Model Catalog と Phase 12 の Setup Wizard で、ASR / 推敲モデルの導入を案内します。現時点では必要ファイルの配置先を表示します。";

    public bool RequiredRuntimeAssetsReady => EnvironmentStatus
        .Where(static item => item.Name is "ffmpeg" or "llama-completion" or "Review model")
        .All(static item => item.IsOk) && IsSelectedAsrEngineReady();

    public bool IsSetupComplete => _setupState.IsCompleted;

    public bool CanRunSelectedJob => SelectedJob is not null && !IsRunInProgress && RequiredRuntimeAssetsReady && IsSetupComplete;

    public ObservableCollection<StatusItem> EnvironmentStatus { get; } = [];

    public ObservableCollection<AsrEngineOption> AvailableAsrEngines { get; } = [];

    public ObservableCollection<JobSummary> Jobs { get; } = [];

    public ObservableCollection<StageStatus> StageStatuses { get; } = [];

    public ObservableCollection<TranscriptSegmentPreview> Segments { get; } = [];

    public ObservableCollection<JobLogEntry> Logs { get; } = [];

    public ObservableCollection<CorrectionDraft> ReviewQueue { get; } = [];

    public ObservableCollection<DiffToken> DiffTokens { get; } = [];

    public ObservableCollection<ModelCatalogEntry> ModelCatalogEntries { get; } = [];

    public ObservableCollection<SetupStepItem> SetupSteps { get; } = [];

    public ObservableCollection<ModelCatalogEntry> SetupAsrModelChoices { get; } = [];

    public ObservableCollection<ModelCatalogEntry> SetupReviewModelChoices { get; } = [];

    public ObservableCollection<SetupSmokeCheck> SetupSmokeChecks { get; } = [];

    public ObservableCollection<SetupModelAudit> SetupModelAudits { get; } = [];

    public ObservableCollection<string> SpeakerFilters { get; } = ["全話者"];

    public ICollectionView FilteredJobs { get; }

    public ICollectionView FilteredSegments { get; }

    public ICommand AddAudioCommand { get; }

    public ICommand DeleteJobCommand { get; }

    public ICommand ClearAllJobsCommand { get; }

    public ICommand RunSelectedJobCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand OpenSetupCommand { get; }

    public ICommand SetupBackCommand { get; }

    public ICommand SetupNextCommand { get; }

    public ICommand SetupUseRecommendedCommand { get; }

    public ICommand SetupAcceptLicensesCommand { get; }

    public ICommand SetupDownloadAsrCommand { get; }

    public ICommand SetupDownloadReviewCommand { get; }

    public ICommand SetupRegisterLocalAsrCommand { get; }

    public ICommand SetupRegisterLocalReviewCommand { get; }

    public ICommand SetupImportOfflinePackCommand { get; }

    public ICommand SetupRunSmokeCommand { get; }

    public ICommand SetupCompleteCommand { get; }

    public ICommand ShowModelCatalogCommand { get; }

    public ICommand RegisterPreinstalledModelsCommand { get; }

    public ICommand UseSelectedModelCommand { get; }

    public ICommand ShowSelectedModelLicenseCommand { get; }

    public ICommand ForgetSelectedModelCommand { get; }

    public ICommand AcceptDraftCommand { get; }

    public ICommand RejectDraftCommand { get; }

    public ICommand ApplyManualEditCommand { get; }

    public ICommand SelectPreviousDraftCommand { get; }

    public ICommand SelectNextDraftCommand { get; }

    public ICommand SaveSegmentEditCommand { get; }

    public ICommand SaveSpeakerAliasCommand { get; }

    public ICommand UndoLastOperationCommand { get; }

    public ICommand ExportSelectedJobCommand { get; }

    public ICommand OpenExportFolderCommand { get; }

    public JobSummary? SelectedJob
    {
        get => _selectedJob;
        set
        {
            if (SetField(ref _selectedJob, value))
            {
                if (RunSelectedJobCommand is RelayCommand command)
                {
                    command.RaiseCanExecuteChanged();
                }

                OnPropertyChanged(nameof(SelectedJobSourcePath));
                OnPropertyChanged(nameof(SelectedJobNormalizedAudioPath));
                OnPropertyChanged(nameof(SelectedJobUpdatedAt));
                OnPropertyChanged(nameof(SelectedJobUnreviewedDrafts));
                RefreshLogs();
                ReloadSegmentsForSelectedJob();
                LoadReviewQueue();
                UpdateExportCommandStates();
            }
        }
    }

    public TranscriptSegmentPreview? SelectedSegment
    {
        get => _selectedSegment;
        set
        {
            if (!SetField(ref _selectedSegment, value))
            {
                return;
            }

            if (value is null)
            {
                SelectedSegmentEditText = string.Empty;
                SelectedSpeakerAlias = string.Empty;
                UpdateSegmentEditCommandStates();
            }
            else
            {
                SelectedSegmentEditText = value.Text;
                SelectedSpeakerAlias = value.Speaker;
                SelectFirstDraftForSegment(value.SegmentId);
                UpdateSegmentEditCommandStates();
            }
        }
    }

    public CorrectionDraft? SelectedCorrectionDraft
    {
        get => _selectedCorrectionDraft;
        set
        {
            if (SetField(ref _selectedCorrectionDraft, value))
            {
                ApplySelectedDraftToReviewPane();
            }
        }
    }

    public ModelCatalogEntry? SelectedModelCatalogEntry
    {
        get => _selectedModelCatalogEntry;
        set
        {
            if (SetField(ref _selectedModelCatalogEntry, value))
            {
                UpdateModelCatalogCommandStates();
            }
        }
    }

    public ModelCatalogEntry? SelectedSetupAsrModel
    {
        get => _selectedSetupAsrModel;
        set
        {
            if (SetField(ref _selectedSetupAsrModel, value) && value is not null)
            {
                ApplySetupModelSelection("asr", value.ModelId);
            }
        }
    }

    public ModelCatalogEntry? SelectedSetupReviewModel
    {
        get => _selectedSetupReviewModel;
        set
        {
            if (SetField(ref _selectedSetupReviewModel, value) && value is not null)
            {
                ApplySetupModelSelection("review", value.ModelId);
            }
        }
    }

    public string SetupCurrentStep => _setupState.CurrentStep.ToString();

    public string SetupStatusSummary => _setupState.IsCompleted
        ? "Setup complete. Run is enabled when runtime assets are ready."
        : $"Setup incomplete. Current step: {_setupState.CurrentStep}. Run is disabled until smoke test and completion pass.";

    public string SetupMode => _setupState.SetupMode;

    public string SetupStorageRoot => _setupState.StorageRoot ?? Paths.UserModels;

    public bool SetupLicenseAccepted => _setupState.LicenseAccepted;

    public string SetupLocalModelPath
    {
        get => _setupLocalModelPath;
        set => SetField(ref _setupLocalModelPath, value ?? string.Empty);
    }

    public string SetupOfflineModelPackPath
    {
        get => _setupOfflineModelPackPath;
        set => SetField(ref _setupOfflineModelPackPath, value ?? string.Empty);
    }

    public string SelectedCorrectionDraftId => SelectedCorrectionDraft?.DraftId ?? string.Empty;

    public string SelectedSegmentEditText
    {
        get => _selectedSegmentEditText;
        set
        {
            if (SetField(ref _selectedSegmentEditText, value ?? string.Empty))
            {
                UpdateSegmentEditCommandStates();
            }
        }
    }

    public string SelectedSpeakerAlias
    {
        get => _selectedSpeakerAlias;
        set
        {
            if (SetField(ref _selectedSpeakerAlias, value ?? string.Empty))
            {
                UpdateSegmentEditCommandStates();
            }
        }
    }

    public string DraftPositionText
    {
        get
        {
            if (SelectedCorrectionDraft is null || ReviewQueue.Count == 0)
            {
                return "0 / 0";
            }

            var index = ReviewQueue.IndexOf(SelectedCorrectionDraft);
            return index < 0 ? "0 / 0" : $"{index + 1} / {ReviewQueue.Count}";
        }
    }

    public string SelectedJobSourcePath => SelectedJob?.SourceAudioPath ?? "";

    public string SelectedJobNormalizedAudioPath => SelectedJob?.NormalizedAudioPath ?? "";

    public string SelectedJobUpdatedAt => SelectedJob?.UpdatedAtDisplay ?? "";

    public int SelectedJobUnreviewedDrafts => SelectedJob?.UnreviewedDrafts ?? 0;

    public string JobCountSummary => $"合計 {Jobs.Count} 件のジョブ";

    public string JobSearchText
    {
        get => _jobSearchText;
        set
        {
            if (SetField(ref _jobSearchText, value))
            {
                FilteredJobs.Refresh();
            }
        }
    }

    public string SegmentSearchText
    {
        get => _segmentSearchText;
        set
        {
            if (SetField(ref _segmentSearchText, value))
            {
                FilteredSegments.Refresh();
            }
        }
    }

    public string SelectedSpeakerFilter
    {
        get => _selectedSpeakerFilter;
        set
        {
            if (SetField(ref _selectedSpeakerFilter, value))
            {
                FilteredSegments.Refresh();
            }
        }
    }

    public bool IsRunInProgress
    {
        get => _isRunInProgress;
        private set
        {
            if (SetField(ref _isRunInProgress, value))
            {
                if (CancelCommand is RelayCommand cancelCommand)
                {
                    cancelCommand.RaiseCanExecuteChanged();
                }

                if (RunSelectedJobCommand is RelayCommand runCommand)
                {
                    runCommand.RaiseCanExecuteChanged();
                }

                if (DeleteJobCommand is RelayCommand<JobSummary> deleteCommand)
                {
                    deleteCommand.RaiseCanExecuteChanged();
                }

                if (ClearAllJobsCommand is RelayCommand clearAllCommand)
                {
                    clearAllCommand.RaiseCanExecuteChanged();
                }

                UpdateReviewCommandStates();
                UpdateSegmentEditCommandStates();
            }
        }
    }

    public bool IsReviewOperationInProgress
    {
        get => _isReviewOperationInProgress;
        private set
        {
            if (SetField(ref _isReviewOperationInProgress, value))
            {
                UpdateReviewCommandStates();
            }
        }
    }

    public bool RememberCorrection
    {
        get => _rememberCorrection;
        set => SetField(ref _rememberCorrection, value);
    }

    public string ExportWarning
    {
        get => _exportWarning;
        private set => SetField(ref _exportWarning, value);
    }

    public string LastExportFolder
    {
        get => _lastExportFolder;
        private set
        {
            if (SetField(ref _lastExportFolder, value))
            {
                UpdateExportCommandStates();
            }
        }
    }

    public string AsrContextText
    {
        get => _asrContextText;
        set
        {
            if (SetField(ref _asrContextText, value ?? string.Empty))
            {
                ScheduleSaveAsrSettings();
            }
        }
    }

    public string AsrHotwordsText
    {
        get => _asrHotwordsText;
        set
        {
            if (SetField(ref _asrHotwordsText, value ?? string.Empty))
            {
                ScheduleSaveAsrSettings();
            }
        }
    }

    public string SelectedAsrEngineId
    {
        get => _selectedAsrEngineId;
        set
        {
            var engineId = string.IsNullOrWhiteSpace(value) ? VibeVoiceCrispAsrEngine.Id : value;
            if (SetField(ref _selectedAsrEngineId, engineId))
            {
                OnPropertyChanged(nameof(AsrModel));
                OnPropertyChanged(nameof(RequiredRuntimeAssetsReady));
                OnPropertyChanged(nameof(CanRunSelectedJob));
                if (RunSelectedJobCommand is RelayCommand runCommand)
                {
                    runCommand.RaiseCanExecuteChanged();
                }

                ScheduleSaveAsrSettings();
            }
        }
    }

    public string ReviewIssueType
    {
        get => _reviewIssueType;
        private set => SetField(ref _reviewIssueType, value);
    }

    public string OriginalText
    {
        get => _originalText;
        private set
        {
            if (SetField(ref _originalText, value))
            {
                RefreshDiffTokens();
            }
        }
    }

    public string SuggestedText
    {
        get => _suggestedText;
        set
        {
            if (SetField(ref _suggestedText, value))
            {
                RefreshDiffTokens();
            }
        }
    }

    public string ReviewReason
    {
        get => _reviewReason;
        private set => SetField(ref _reviewReason, value);
    }

    public double Confidence
    {
        get => _confidence;
        private set => SetField(ref _confidence, value);
    }

    public string LatestLog
    {
        get => _latestLog;
        private set => SetField(ref _latestLog, value);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private Task OpenSetupAsync()
    {
        RefreshModelCatalog();
        RefreshSetupWizard();
        LatestLog = $"{SetupPlaceholderText}{Environment.NewLine}Setup step: {SetupCurrentStep}. Model catalog: {ModelCatalogEntries.Count} entries.";
        return Task.CompletedTask;
    }

    private Task ShowModelCatalogAsync()
    {
        RefreshModelCatalog();
        var installedCount = ModelCatalogEntries.Count(static entry => entry.IsInstalled);
        LatestLog = $"Model catalog loaded: {ModelCatalogEntries.Count} entries, {installedCount} installed. Select a model in the Models tab for use/license/forget actions.";
        return Task.CompletedTask;
    }

    private Task RegisterPreinstalledModelsAsync()
    {
        var catalog = _modelCatalogService.LoadBuiltInCatalog();
        RegisterIfPresent(catalog, "vibevoice-asr-q4-k", Paths.VibeVoiceAsrModelPath, "preinstalled");
        RegisterIfPresent(catalog, "llm-jp-4-8b-thinking-q4-k-m", Paths.ReviewModelPath, "preinstalled");
        RefreshModelCatalog();
        LatestLog = "Preinstalled model scan completed.";
        return Task.CompletedTask;
    }

    private Task UseSelectedModelAsync()
    {
        if (SelectedModelCatalogEntry is null)
        {
            return Task.CompletedTask;
        }

        if (SelectedModelCatalogEntry.Role.Equals("asr", StringComparison.OrdinalIgnoreCase))
        {
            SelectedAsrEngineId = SelectedModelCatalogEntry.EngineId;
            LatestLog = $"ASR model selected: {SelectedModelCatalogEntry.DisplayName} ({SelectedModelCatalogEntry.EngineId})";
        }
        else
        {
            LatestLog = $"Review model selected for Phase 12 setup: {SelectedModelCatalogEntry.DisplayName}";
        }

        return Task.CompletedTask;
    }

    private Task ShowSelectedModelLicenseAsync()
    {
        if (SelectedModelCatalogEntry is not null)
        {
            LatestLog = $"""
                {_modelLicenseViewer.BuildLicenseSummary(SelectedModelCatalogEntry.ModelId)}
                Size: {SelectedModelCatalogEntry.SizeSummary}
                Requirements: {SelectedModelCatalogEntry.RuntimeRequirement}
                Install: {SelectedModelCatalogEntry.InstallState}
                Download: {SelectedModelCatalogEntry.DownloadState}
                """;
        }

        return Task.CompletedTask;
    }

    private Task ForgetSelectedModelAsync()
    {
        if (SelectedModelCatalogEntry is not null &&
            _modelInstallService.DeleteRegistration(SelectedModelCatalogEntry.ModelId))
        {
            LatestLog = $"Model registration removed: {SelectedModelCatalogEntry.DisplayName}";
            RefreshModelCatalog();
        }

        return Task.CompletedTask;
    }

    private void RefreshModelCatalog()
    {
        ModelCatalogEntries.Clear();
        foreach (var entry in _modelCatalogService.ListEntries())
        {
            ModelCatalogEntries.Add(entry);
        }

        SelectedModelCatalogEntry ??= ModelCatalogEntries.FirstOrDefault();
        UpdateModelCatalogCommandStates();
    }

    private void RegisterIfPresent(ModelCatalog catalog, string modelId, string path, string sourceType)
    {
        var item = catalog.Models.FirstOrDefault(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (item is not null && (File.Exists(path) || Directory.Exists(path)))
        {
            _modelInstallService.RegisterLocalModel(item, path, sourceType);
        }
    }

    private bool IsSelectedAsrEngineReady()
    {
        return SelectedAsrEngineId switch
        {
            VibeVoiceCrispAsrEngine.Id => File.Exists(Paths.CrispAsrPath) && File.Exists(Paths.VibeVoiceAsrModelPath),
            "faster-whisper-large-v3-turbo" => File.Exists(Paths.FasterWhisperScriptPath) &&
                ModelPathExists("faster-whisper-large-v3-turbo", Paths.FasterWhisperModelPath),
            "reazonspeech-k2-v3" => File.Exists(Paths.ReazonSpeechK2ScriptPath) &&
                ModelPathExists("reazonspeech-k2-v3-ja", Paths.ReazonSpeechK2ModelPath),
            _ => false
        };
    }

    private bool ModelPathExists(string modelId, string fallbackPath)
    {
        var installed = _installedModelRepository.FindInstalledModel(modelId);
        if (installed is not null &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
        {
            return true;
        }

        return File.Exists(fallbackPath) || Directory.Exists(fallbackPath);
    }

    private void UpdateModelCatalogCommandStates()
    {
        if (UseSelectedModelCommand is RelayCommand useCommand)
        {
            useCommand.RaiseCanExecuteChanged();
        }

        if (ShowSelectedModelLicenseCommand is RelayCommand licenseCommand)
        {
            licenseCommand.RaiseCanExecuteChanged();
        }

        if (ForgetSelectedModelCommand is RelayCommand forgetCommand)
        {
            forgetCommand.RaiseCanExecuteChanged();
        }
    }

    private async Task RefreshStatusBarInfoAsync()
    {
        if (_isStatusRefreshInProgress)
        {
            return;
        }

        try
        {
            _isStatusRefreshInProgress = true;
            _statusBarInfo = await Task.Run(_statusBarInfoService.GetStatusBarInfo);
            OnPropertyChanged(nameof(DiskFreeSummary));
            OnPropertyChanged(nameof(MemorySummary));
            OnPropertyChanged(nameof(CpuSummary));
            OnPropertyChanged(nameof(GpuUsageSummary));
        }
        finally
        {
            _isStatusRefreshInProgress = false;
        }
    }

    private static IEnumerable<StageStatus> CreateStageStatuses()
    {
        yield return new StageStatus(
            "音声変換",
            "M4,12 C5.1,12 5.1,7 6.2,7 C7.4,7 7.3,17 8.5,17 C9.7,17 9.7,9 10.9,9 C12.1,9 12.1,15 13.3,15 C14.5,15 14.5,10 15.7,10 C16.9,10 16.9,14 18.1,14 C19.1,14 19.3,12 20,12 M4,5 L7,5 M5.5,3.5 L5.5,6.5 M17,5 L20,5 M18.5,3.5 L18.5,6.5",
            "#2F8F5B",
            "#EAF6EF");

        yield return new StageStatus(
            "ASR",
            "M8,6 C8,3.8 9.8,2.5 12,2.5 C14.2,2.5 16,3.8 16,6 L16,11 C16,13.2 14.2,14.5 12,14.5 C9.8,14.5 8,13.2 8,11 Z M5.5,10 C5.5,14 8.2,17 12,17 C15.8,17 18.5,14 18.5,10 M12,17 L12,21 M9,21 L15,21",
            "#2563EB",
            "#EFF6FF");

        yield return new StageStatus(
            "推敲",
            "M5,16.5 L4,20 L7.5,19 L17.8,8.7 C18.6,7.9 18.6,6.7 17.8,5.9 L16.1,4.2 C15.3,3.4 14.1,3.4 13.3,4.2 Z M12.5,5 L17,9.5 M17.5,15 L20,15 M18.75,13.75 L18.75,16.25 M6.5,6.5 L8.5,6.5 M7.5,5.5 L7.5,7.5",
            "#7C3AED",
            "#F3E8FF");

        yield return new StageStatus(
            "レビュー",
            "M6,3.5 L15,3.5 L19,7.5 L19,20.5 L6,20.5 Z M15,3.5 L15,7.5 L19,7.5 M8.5,13 L11,15.5 L15.8,10.7 M8.5,18 L15.5,18",
            "#D97706",
            "#FEF3C7");

        yield return new StageStatus(
            "出力",
            "M5,5 L13.5,5 M13.5,5 L13.5,9.5 M13.5,5 L4.5,14 M8,10 L8,20 L20,20 L20,8 L16,8 M13,15 L17,15 M17,15 L15,13 M17,15 L15,17",
            "#0F766E",
            "#CCFBF1");
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
