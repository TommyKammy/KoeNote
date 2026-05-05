using System.Collections.ObjectModel;
using System.ComponentModel;
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
    private const string DefaultSelectableAsrEngineId = "kotoba-whisper-v2.2-faster";

    private readonly JobRepository _jobRepository;
    private readonly JobLogRepository _jobLogRepository;
    private readonly AsrSettingsRepository _asrSettingsRepository;
    private readonly TranscriptSegmentRepository _transcriptSegmentRepository;
    private readonly AsrEngineRegistry _asrEngineRegistry;
    private readonly JobRunCoordinator _jobRunCoordinator;
    private readonly CorrectionDraftRepository _correctionDraftRepository;
    private readonly ReviewOperationService _reviewOperationService;
    private readonly TranscriptEditService _transcriptEditService;
    private readonly CorrectionMemoryService _correctionMemoryService;
    private readonly TranscriptExportService _transcriptExportService;
    private readonly ModelCatalogService _modelCatalogService;
    private readonly InstalledModelRepository _installedModelRepository;
    private readonly ModelInstallService _modelInstallService;
    private readonly ModelDownloadService _modelDownloadService;
    private readonly ModelLicenseViewer _modelLicenseViewer;
    private readonly SetupWizardService _setupWizardService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly TextDiffService _textDiffService = new();
    private readonly StatusBarInfoService _statusBarInfoService;
    private readonly DatabaseMaintenanceService _databaseMaintenanceService;
    private readonly DispatcherTimer _statusRefreshTimer;
    private readonly DispatcherTimer _playbackRefreshTimer;
    private readonly DispatcherTimer _modelDownloadNotificationTimer;
    private StatusBarInfo _statusBarInfo;
    private bool _isStatusRefreshInProgress;
    private bool _isRefreshingPlaybackPosition;
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
    private CancellationTokenSource? _modelDownloadCancellation;
    private CancellationTokenSource? _asrSettingsSaveDebounce;
    private bool _isReviewOperationInProgress;
    private bool _isSelectingSegmentForDraft;
    private bool _isAudioPlaying;
    private bool _rememberCorrection = true;
    private double _playbackPositionSeconds;
    private double _playbackDurationSeconds;
    private double _playbackRate = 1.0;
    private double _playbackVolume = 1.0;
    private string _exportWarning = string.Empty;
    private string _lastExportFolder = string.Empty;
    private bool _includeExportTimestamps = true;
    private string _latestLog;
    private string _modelDownloadProgressSummary = "No active model download.";
    private string _modelDownloadNotification = string.Empty;
    private bool _isModelDownloadNotificationError;
    private string _setupDiarizationRuntimeSummary = "話者識別ランタイムは未導入です。必要になったらここから追加できます。";
    private string _databaseMaintenanceSummary = string.Empty;
    private bool _isDatabaseMaintenanceInProgress;
    private double _modelDownloadProgressPercent;
    private bool _isModelDownloadInProgress;
    private bool _isModelDownloadProgressIndeterminate;
    private string _jobSearchText = string.Empty;
    private string _segmentSearchText = string.Empty;
    private int _selectedLogPanelTabIndex;
    private int _selectedDetailPanelTabIndex;
    private bool _isDetailPanelOpen;
    private bool _isSetupWizardModalOpen;
    private string? _activeModelDownloadModelId;
    private string _selectedSpeakerFilter = "全話者";
    private string _asrContextText = string.Empty;
    private string _asrHotwordsText = string.Empty;
    private string _selectedAsrEngineId = VibeVoiceCrispAsrEngine.Id;
    private bool _enableReviewStage = true;
    private string _selectedSegmentEditText = string.Empty;
    private string _selectedSpeakerAlias = string.Empty;
    private bool _isRunInProgress;
    private string _reviewIssueType = "候補なし";
    private string _originalText = string.Empty;
    private string _suggestedText = string.Empty;
    private string _reviewReason = "推敲候補はありません。";
    private double _confidence;
    private int _reviewSegmentFocusRequestId;

    public MainWindowViewModel()
        : this(new AppPaths())
    {
    }

    public MainWindowViewModel(AppPaths paths)
        : this(paths, MainWindowServices.Create(paths))
    {
    }

    internal MainWindowViewModel(AppPaths paths, MainWindowServices services)
    {
        Paths = paths;
        _jobRepository = services.JobRepository;
        _jobLogRepository = services.JobLogRepository;
        _asrSettingsRepository = services.AsrSettingsRepository;
        _correctionDraftRepository = services.CorrectionDraftRepository;
        _reviewOperationService = services.ReviewOperationService;
        _transcriptEditService = services.TranscriptEditService;
        _correctionMemoryService = services.CorrectionMemoryService;
        _transcriptExportService = services.TranscriptExportService;
        _modelCatalogService = services.ModelCatalogService;
        _installedModelRepository = services.InstalledModelRepository;
        _modelInstallService = services.ModelInstallService;
        _modelDownloadService = services.ModelDownloadService;
        _modelLicenseViewer = services.ModelLicenseViewer;
        _setupWizardService = services.SetupWizardService;
        _audioPlaybackService = services.AudioPlaybackService;
        _audioPlaybackService.PlaybackStateChanged += (_, _) =>
        {
            IsAudioPlaying = _audioPlaybackService.IsPlaying;
            RefreshPlaybackState();
        };
        _setupState = _setupWizardService.LoadState();
        _transcriptSegmentRepository = services.TranscriptSegmentRepository;
        _asrEngineRegistry = services.AsrEngineRegistry;
        _jobRunCoordinator = services.JobRunCoordinator;
        _statusBarInfoService = services.StatusBarInfoService;
        _databaseMaintenanceService = services.DatabaseMaintenanceService;
        _statusBarInfo = _statusBarInfoService.GetStatusBarInfo();
        _statusRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _statusRefreshTimer.Tick += async (_, _) => await RefreshStatusBarInfoAsync();
        _statusRefreshTimer.Start();
        _playbackRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _playbackRefreshTimer.Tick += (_, _) => RefreshPlaybackState();
        _playbackRefreshTimer.Start();
        _modelDownloadNotificationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _modelDownloadNotificationTimer.Tick += (_, _) =>
        {
            _modelDownloadNotificationTimer.Stop();
            ClearModelDownloadNotification();
        };

        foreach (var item in services.ToolStatusService.GetStatusItems())
        {
            EnvironmentStatus.Add(item);
        }

        foreach (var stageStatus in CreateStageStatuses())
        {
            StageStatuses.Add(stageStatus);
        }

        _latestLog = $"Initialized AppData at {Paths.Root}";
        foreach (var engine in _asrEngineRegistry.Engines.Where(static engine => IsUserSelectableAsrEngine(engine.EngineId)))
        {
            AvailableAsrEngines.Add(new AsrEngineOption(engine.EngineId, engine.DisplayName));
        }

        var asrSettings = _asrSettingsRepository.Load();
        _asrContextText = asrSettings.ContextText;
        _asrHotwordsText = asrSettings.HotwordsText;
        _enableReviewStage = asrSettings.EnableReviewStage;
        _selectedAsrEngineId = ResolveInitialAsrEngineId(asrSettings.EngineId);
        FilteredJobs = CollectionViewSource.GetDefaultView(Jobs);
        FilteredJobs.Filter = FilterJob;
        FilteredDeletedJobs = CollectionViewSource.GetDefaultView(DeletedJobs);
        FilteredDeletedJobs.Filter = FilterJob;
        FilteredSegments = CollectionViewSource.GetDefaultView(Segments);
        FilteredSegments.Filter = FilterSegment;
        LoadJobs();
        RefreshLogs();

        AddAudioCommand = new RelayCommand(AddAudioAsync);
        DeleteJobCommand = new RelayCommand<JobSummary>(DeleteJobAsync, job => job is not null && !IsRunInProgress);
        ClearAllJobsCommand = new RelayCommand(ClearAllJobsAsync, () => Jobs.Count > 0 && !IsRunInProgress);
        RestoreJobCommand = new RelayCommand<JobSummary>(RestoreJobAsync, job => job is not null && !IsRunInProgress);
        PermanentlyDeleteJobCommand = new RelayCommand<JobSummary>(PermanentlyDeleteJobAsync, job => job is not null && !IsRunInProgress);
        PermanentlyDeleteAllDeletedJobsCommand = new RelayCommand(PermanentlyDeleteAllDeletedJobsAsync, () => DeletedJobs.Count > 0 && !IsRunInProgress);
        RunSelectedJobCommand = new RelayCommand(RunSelectedJobAsync, () => CanRunSelectedJob);
        CancelCommand = new RelayCommand(CancelRunAsync, () => IsRunInProgress);
        PlayPauseAudioCommand = new RelayCommand(PlayPauseAudioAsync, CanPlaySelectedJobAudio);
        OpenSettingsCommand = new RelayCommand(OpenSettingsAsync);
        OpenSetupCommand = new RelayCommand(OpenSetupAsync);
        CloseSetupWizardModalCommand = new RelayCommand(CloseSetupWizardModalAsync);
        OpenSelectedDetailPanelCommand = new RelayCommand(OpenSelectedDetailPanelAsync, CanOpenSelectedDetailPanel);
        CloseDetailPanelCommand = new RelayCommand(CloseDetailPanelAsync);
        SetupBackCommand = new RelayCommand(SetupBackAsync);
        SetupNextCommand = new RelayCommand(SetupNextAsync);
        SetupUseRecommendedCommand = new RelayCommand(SetupUseRecommendedAsync);
        SetupAcceptLicensesCommand = new RelayCommand(SetupAcceptLicensesAsync);
        SetupDownloadAsrCommand = new RelayCommand(SetupDownloadAsrAsync);
        SetupDownloadReviewCommand = new RelayCommand(SetupDownloadReviewAsync);
        SetupInstallDiarizationRuntimeCommand = new RelayCommand(SetupInstallDiarizationRuntimeAsync);
        SetupRegisterLocalAsrCommand = new RelayCommand(SetupRegisterLocalAsrAsync);
        SetupRegisterLocalReviewCommand = new RelayCommand(SetupRegisterLocalReviewAsync);
        SetupImportOfflinePackCommand = new RelayCommand(SetupImportOfflinePackAsync);
        SetupChooseStorageRootCommand = new RelayCommand(SetupChooseStorageRootAsync);
        SetupRunSmokeCommand = new RelayCommand(SetupRunSmokeAsync);
        SetupCompleteCommand = new RelayCommand(SetupCompleteAsync);
        ShowModelCatalogCommand = new RelayCommand(ShowModelCatalogAsync);
        RegisterPreinstalledModelsCommand = new RelayCommand(RegisterPreinstalledModelsAsync);
        DownloadSelectedModelCommand = new RelayCommand(DownloadSelectedModelAsync, CanDownloadSelectedModel);
        PauseSelectedModelDownloadCommand = new RelayCommand(PauseSelectedModelDownloadAsync, CanPauseSelectedModelDownload);
        ResumeSelectedModelDownloadCommand = new RelayCommand(ResumeSelectedModelDownloadAsync, CanResumeSelectedModelDownload);
        CancelSelectedModelDownloadCommand = new RelayCommand(CancelSelectedModelDownloadAsync, CanCancelSelectedModelDownload);
        RetrySelectedModelDownloadCommand = new RelayCommand(RetrySelectedModelDownloadAsync, CanRetrySelectedModelDownload);
        UseSelectedModelCommand = new RelayCommand(UseSelectedModelAsync, () => SelectedModelCatalogEntry is not null);
        ShowSelectedModelLicenseCommand = new RelayCommand(ShowSelectedModelLicenseAsync, () => SelectedModelCatalogEntry is not null);
        ForgetSelectedModelCommand = new RelayCommand(ForgetSelectedModelAsync, () => SelectedModelCatalogEntry?.IsInstalled == true);
        AcceptDraftCommand = new RelayCommand(AcceptSelectedDraftAsync, CanOperateOnSelectedDraft);
        RejectDraftCommand = new RelayCommand(RejectSelectedDraftAsync, CanOperateOnSelectedDraft);
        ApplyManualEditCommand = new RelayCommand(ApplyManualEditAsync, CanOperateOnSelectedDraft);
        SelectPreviousDraftCommand = new RelayCommand(SelectPreviousDraftAsync, CanSelectPreviousDraft);
        SelectNextDraftCommand = new RelayCommand(SelectNextDraftAsync, CanSelectNextDraft);
        FocusSelectedDraftSegmentCommand = new RelayCommand(FocusSelectedDraftSegmentAsync, () => SelectedCorrectionDraft is not null);
        SaveSegmentEditCommand = new RelayCommand(SaveSegmentEditAsync, CanEditSelectedSegment);
        SaveSpeakerAliasCommand = new RelayCommand(SaveSpeakerAliasAsync, CanEditSelectedSpeaker);
        UndoLastOperationCommand = new RelayCommand(UndoLastOperationAsync);
        ExportSelectedJobCommand = new RelayCommand(ExportSelectedJobAsync, CanExportSelectedJob);
        ExportTxtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Text), CanExportSelectedJob);
        ExportJsonCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Json), CanExportSelectedJob);
        ExportSrtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Srt), CanExportSelectedJob);
        ExportDocxCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Docx), CanExportSelectedJob);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolderAsync, CanOpenExportFolder);
        RunDatabaseMaintenanceCommand = new RelayCommand(RunDatabaseMaintenanceAsync, CanRunDatabaseMaintenance);

        RefreshModelCatalog();
        RefreshSetupWizard();
        IsSetupWizardModalOpen = !IsSetupComplete;
        RefreshSpeakerFilters();
        RefreshDatabaseMaintenanceSummary();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppPaths Paths { get; }

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

    public string SetupDiarizationRuntimeSummary
    {
        get => _setupDiarizationRuntimeSummary;
        private set => SetField(ref _setupDiarizationRuntimeSummary, value);
    }

    public bool RequiredRuntimeAssetsReady => EnvironmentStatus
        .Where(item => item.Name == "ffmpeg" || (EnableReviewStage && item.Name == "llama-completion"))
        .All(static item => item.IsOk) &&
        IsSelectedAsrEngineReady() &&
        (!EnableReviewStage || IsReviewModelReady());

    public bool IsSetupComplete => _setupState.IsCompleted;

    public bool IsSetupWizardModalOpen
    {
        get => _isSetupWizardModalOpen;
        private set => SetField(ref _isSetupWizardModalOpen, value);
    }

    public string SetupWizardModalTitle => _setupState.CurrentStep switch
    {
        SetupStep.Welcome => "KoeNote へようこそ",
        SetupStep.EnvironmentCheck => "まず動作環境を確認します",
        SetupStep.SetupMode => "使い方に合わせて準備します",
        SetupStep.AsrModel => "文字起こしモデルを選びます",
        SetupStep.ReviewModel => "推敲モデルを選びます",
        SetupStep.Storage => "モデルの保存先を確認します",
        SetupStep.License => "ライセンスを確認します",
        SetupStep.Install => "モデルを導入します",
        SetupStep.SmokeTest => "最後に動作確認します",
        SetupStep.Complete => "準備完了です",
        _ => "初回セットアップ"
    };

    public string SetupWizardModalGuide => _setupState.CurrentStep switch
    {
        SetupStep.Welcome => "KoeNote は本体だけ先に起動し、ASR / 推敲モデルはあとから導入します。ここでは最初の文字起こしに必要な準備を順番に案内します。",
        SetupStep.EnvironmentCheck => "足りない runtime やモデルがあってもアプリ本体は壊れません。ここで次に必要な導入操作を確認できます。",
        SetupStep.SetupMode => "迷ったら Recommended を選んでください。高精度 ASR だけで始めたい場合は、あとから推敲をオフにできます。",
        SetupStep.AsrModel => "日本語文字起こしには faster-whisper large-v3-turbo を推奨します。精度優先なら large-v3 も選べます。",
        SetupStep.ReviewModel => "推敲は文字起こし結果の確認を助ける追加ステージです。不要な場合は Settings でいつでもスキップできます。",
        SetupStep.Storage => "オンラインダウンロード、ローカルファイル、offline model pack のどれでも導入できます。",
        SetupStep.License => "モデルごとの license / size / runtime requirement を確認してから導入します。",
        SetupStep.Install => "ダウンロード中は進捗を表示します。失敗しても本体は起動したままで、別経路から再試行できます。",
        SetupStep.SmokeTest => "ネットワークなしで startup、sample import、review screen、export path を確認します。",
        SetupStep.Complete => "セットアップが完了すると Run が有効になります。通常画面からいつでも Setup / Models を開けます。",
        _ => "KoeNote の初回利用に必要な準備を案内します。"
    };

    public bool CanRunSelectedJob => SelectedJob is not null && !IsRunInProgress && RequiredRuntimeAssetsReady && IsSetupComplete;

    public ObservableCollection<StatusItem> EnvironmentStatus { get; } = [];

    public ObservableCollection<AsrEngineOption> AvailableAsrEngines { get; } = [];

    public ObservableCollection<JobSummary> Jobs { get; } = [];

    public ObservableCollection<JobSummary> DeletedJobs { get; } = [];

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

    public ObservableCollection<SetupExistingDataItem> SetupExistingData { get; } = [];

    public ObservableCollection<double> PlaybackRates { get; } = [1.0, 1.25, 1.5, 2.0];

    public ObservableCollection<double> PlaybackWaveformSamples { get; } = [];

    public ObservableCollection<string> SpeakerFilters { get; } = ["全話者"];

    public ICollectionView FilteredJobs { get; }

    public ICollectionView FilteredDeletedJobs { get; }

    public ICollectionView FilteredSegments { get; }

    public ICommand AddAudioCommand { get; }

    public ICommand DeleteJobCommand { get; }

    public ICommand ClearAllJobsCommand { get; }

    public ICommand RestoreJobCommand { get; }

    public ICommand PermanentlyDeleteJobCommand { get; }

    public ICommand PermanentlyDeleteAllDeletedJobsCommand { get; }

    public ICommand RunSelectedJobCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand PlayPauseAudioCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenSetupCommand { get; }

    public ICommand CloseSetupWizardModalCommand { get; }

    public ICommand OpenSelectedDetailPanelCommand { get; }

    public ICommand CloseDetailPanelCommand { get; }

    public ICommand SetupBackCommand { get; }

    public ICommand SetupNextCommand { get; }

    public ICommand SetupUseRecommendedCommand { get; }

    public ICommand SetupAcceptLicensesCommand { get; }

    public ICommand SetupDownloadAsrCommand { get; }

    public ICommand SetupDownloadReviewCommand { get; }

    public ICommand SetupInstallDiarizationRuntimeCommand { get; }

    public ICommand SetupRegisterLocalAsrCommand { get; }

    public ICommand SetupRegisterLocalReviewCommand { get; }

    public ICommand SetupImportOfflinePackCommand { get; }

    public ICommand SetupChooseStorageRootCommand { get; }

    public ICommand SetupRunSmokeCommand { get; }

    public ICommand SetupCompleteCommand { get; }

    public ICommand ShowModelCatalogCommand { get; }

    public ICommand RegisterPreinstalledModelsCommand { get; }

    public ICommand DownloadSelectedModelCommand { get; }

    public ICommand PauseSelectedModelDownloadCommand { get; }

    public ICommand ResumeSelectedModelDownloadCommand { get; }

    public ICommand CancelSelectedModelDownloadCommand { get; }

    public ICommand RetrySelectedModelDownloadCommand { get; }

    public ICommand UseSelectedModelCommand { get; }

    public ICommand ShowSelectedModelLicenseCommand { get; }

    public ICommand ForgetSelectedModelCommand { get; }

    public ICommand AcceptDraftCommand { get; }

    public ICommand RejectDraftCommand { get; }

    public ICommand ApplyManualEditCommand { get; }

    public ICommand SelectPreviousDraftCommand { get; }

    public ICommand SelectNextDraftCommand { get; }

    public ICommand FocusSelectedDraftSegmentCommand { get; }

    public ICommand SaveSegmentEditCommand { get; }

    public ICommand SaveSpeakerAliasCommand { get; }

    public ICommand UndoLastOperationCommand { get; }

    public ICommand ExportSelectedJobCommand { get; }

    public ICommand ExportTxtCommand { get; }

    public ICommand ExportJsonCommand { get; }

    public ICommand ExportSrtCommand { get; }

    public ICommand ExportDocxCommand { get; }

    public ICommand OpenExportFolderCommand { get; }

    public ICommand RunDatabaseMaintenanceCommand { get; }

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
                OnPropertyChanged(nameof(SelectedJobPlaybackPath));
                OnPropertyChanged(nameof(SelectedJobUpdatedAt));
                OnPropertyChanged(nameof(SelectedJobUnreviewedDrafts));
                RefreshPlaybackWaveform();
                StopAudioPlayback();
                RefreshLogs();
                ReloadSegmentsForSelectedJob();
                LoadReviewQueue();
                UpdateExportCommandStates();
                UpdatePlaybackCommandStates();
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
                if (!_isSelectingSegmentForDraft)
                {
                    SelectFirstDraftForSegment(value.SegmentId);
                }

                UpdateSegmentEditCommandStates();
                SeekPlaybackToSelectedSegment(value);
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
                OnPropertyChanged(nameof(HasReviewDraft));
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
        ? "セットアップは完了しています。必要なモデルと実行環境が揃っていれば実行できます。"
        : $"セットアップは未完了です。現在のステップ: {SetupStepDisplayName}。モデル導入、ライセンス同意、最終確認を完了すると実行できます。";

    public string SetupStepDisplayName => _setupState.CurrentStep switch
    {
        SetupStep.Welcome => "ようこそ",
        SetupStep.EnvironmentCheck => "環境確認",
        SetupStep.SetupMode => "セットアップ方式",
        SetupStep.AsrModel => "ASRモデル",
        SetupStep.ReviewModel => "推敲LLM",
        SetupStep.Storage => "保存先",
        SetupStep.License => "ライセンス",
        SetupStep.Install => "モデル導入",
        SetupStep.SmokeTest => "最終確認",
        SetupStep.Complete => "完了",
        _ => _setupState.CurrentStep.ToString()
    };

    public string SetupMode => _setupState.SetupMode;

    public string SetupStorageRoot => _setupState.StorageRoot ?? Paths.DefaultModelStorageRoot;

    public bool SetupLicenseAccepted => _setupState.LicenseAccepted;

    public string ModelDownloadProgressSummary
    {
        get => _modelDownloadProgressSummary;
        private set => SetField(ref _modelDownloadProgressSummary, value);
    }

    public double ModelDownloadProgressPercent
    {
        get => _modelDownloadProgressPercent;
        private set
        {
            if (SetField(ref _modelDownloadProgressPercent, Math.Clamp(value, 0, 100)))
            {
                OnPropertyChanged(nameof(ModelDownloadProgressText));
            }
        }
    }

    public bool IsModelDownloadInProgress
    {
        get => _isModelDownloadInProgress;
        private set => SetField(ref _isModelDownloadInProgress, value);
    }

    public bool IsModelDownloadProgressIndeterminate
    {
        get => _isModelDownloadProgressIndeterminate;
        private set
        {
            if (SetField(ref _isModelDownloadProgressIndeterminate, value))
            {
                OnPropertyChanged(nameof(ModelDownloadProgressText));
            }
        }
    }

    public string ModelDownloadProgressText => IsModelDownloadProgressIndeterminate
        ? "計算中"
        : $"{ModelDownloadProgressPercent:0}%";

    public string ModelDownloadNotification
    {
        get => _modelDownloadNotification;
        private set
        {
            if (SetField(ref _modelDownloadNotification, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(HasModelDownloadNotification));
            }
        }
    }

    public bool HasModelDownloadNotification => !string.IsNullOrWhiteSpace(ModelDownloadNotification);

    public bool IsModelDownloadNotificationError
    {
        get => _isModelDownloadNotificationError;
        private set => SetField(ref _isModelDownloadNotificationError, value);
    }

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

    public bool HasReviewDraft => SelectedCorrectionDraft is not null;

    public int ReviewSegmentFocusRequestId
    {
        get => _reviewSegmentFocusRequestId;
        private set => SetField(ref _reviewSegmentFocusRequestId, value);
    }

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

    public string SelectedJobPlaybackPath => ResolveSelectedJobPlaybackPath() ?? "";

    public string SelectedJobUpdatedAt => SelectedJob?.UpdatedAtDisplay ?? "";

    public int SelectedJobUnreviewedDrafts => SelectedJob?.UnreviewedDrafts ?? 0;

    public string JobCountSummary => $"合計 {Jobs.Count} 件のジョブ";

    public string DeletedJobCountSummary => $"履歴 {DeletedJobs.Count} 件 / {FormatByteSize(DeletedJobs.Sum(static job => job.StorageBytes))}";

    public string JobSearchText
    {
        get => _jobSearchText;
        set
        {
            if (SetField(ref _jobSearchText, value))
            {
                FilteredJobs.Refresh();
                FilteredDeletedJobs.Refresh();
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

    public int SelectedLogPanelTabIndex
    {
        get => _selectedLogPanelTabIndex;
        set
        {
            if (SetField(ref _selectedLogPanelTabIndex, Math.Clamp(value, 0, 3)) &&
                OpenSelectedDetailPanelCommand is RelayCommand openWideCommand)
            {
                openWideCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public int SelectedDetailPanelTabIndex
    {
        get => _selectedDetailPanelTabIndex;
        set
        {
            if (SetField(ref _selectedDetailPanelTabIndex, Math.Clamp(value, 0, 2)))
            {
                OnPropertyChanged(nameof(DetailPanelTitle));
                SelectedLogPanelTabIndex = _selectedDetailPanelTabIndex + 1;
            }
        }
    }

    public bool IsDetailPanelOpen
    {
        get => _isDetailPanelOpen;
        private set => SetField(ref _isDetailPanelOpen, value);
    }

    public string DetailPanelTitle => SelectedDetailPanelTabIndex switch
    {
        0 => "設定",
        1 => "セットアップ / モデル導入",
        2 => "モデル",
        _ => "詳細"
    };

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

                if (RestoreJobCommand is RelayCommand<JobSummary> restoreCommand)
                {
                    restoreCommand.RaiseCanExecuteChanged();
                }

                if (PermanentlyDeleteJobCommand is RelayCommand<JobSummary> purgeCommand)
                {
                    purgeCommand.RaiseCanExecuteChanged();
                }

                if (PermanentlyDeleteAllDeletedJobsCommand is RelayCommand purgeAllCommand)
                {
                    purgeAllCommand.RaiseCanExecuteChanged();
                }

                if (RunDatabaseMaintenanceCommand is RelayCommand maintenanceCommand)
                {
                    maintenanceCommand.RaiseCanExecuteChanged();
                }

                UpdateReviewCommandStates();
                UpdateSegmentEditCommandStates();
            }
        }
    }

    public bool IsAudioPlaying
    {
        get => _isAudioPlaying;
        private set
        {
            if (SetField(ref _isAudioPlaying, value))
            {
                OnPropertyChanged(nameof(PlayPauseAudioIcon));
            }
        }
    }

    public string PlayPauseAudioIcon => IsAudioPlaying ? "\uE769" : "\uE768";

    public double PlaybackPositionSeconds
    {
        get => _playbackPositionSeconds;
        set
        {
            var nextValue = Math.Clamp(value, 0, PlaybackDurationSeconds > 0 ? PlaybackDurationSeconds : double.MaxValue);
            if (Math.Abs(_playbackPositionSeconds - nextValue) < 0.05)
            {
                return;
            }

            _playbackPositionSeconds = nextValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackTimeDisplay));

            if (!_isRefreshingPlaybackPosition)
            {
                _audioPlaybackService.Seek(TimeSpan.FromSeconds(nextValue));
            }
        }
    }

    public double PlaybackDurationSeconds
    {
        get => _playbackDurationSeconds;
        private set
        {
            if (Math.Abs(_playbackDurationSeconds - value) < 0.05)
            {
                return;
            }

            _playbackDurationSeconds = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackTimeDisplay));
        }
    }

    public double PlaybackRate
    {
        get => _playbackRate;
        set
        {
            if (Math.Abs(_playbackRate - value) < 0.001)
            {
                return;
            }

            _playbackRate = value > 0 ? value : 1.0;
            OnPropertyChanged();
            _audioPlaybackService.SetPlaybackRate(_playbackRate);
        }
    }

    public double PlaybackVolume
    {
        get => _playbackVolume;
        set
        {
            var nextValue = Math.Clamp(value, 0, 1);
            if (Math.Abs(_playbackVolume - nextValue) < 0.001)
            {
                return;
            }

            _playbackVolume = nextValue;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlaybackVolumeIcon));
            _audioPlaybackService.SetVolume(_playbackVolume);
        }
    }

    public string PlaybackVolumeIcon => PlaybackVolume <= 0.001 ? "\uE74F" : "\uE767";

    public string PlaybackTimeDisplay =>
        $"{FormatPlaybackTime(TimeSpan.FromSeconds(PlaybackPositionSeconds))} / {FormatPlaybackTime(TimeSpan.FromSeconds(PlaybackDurationSeconds))}";

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

    public bool IncludeExportTimestamps
    {
        get => _includeExportTimestamps;
        set => SetField(ref _includeExportTimestamps, value);
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
            var engineId = IsUserSelectableAsrEngine(value) ? value : DefaultSelectableAsrEngineId;
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

    public bool EnableReviewStage
    {
        get => _enableReviewStage;
        set
        {
            if (SetField(ref _enableReviewStage, value))
            {
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

    public string DatabaseMaintenanceSummary
    {
        get => _databaseMaintenanceSummary;
        private set => SetField(ref _databaseMaintenanceSummary, value);
    }

    public bool IsDatabaseMaintenanceInProgress
    {
        get => _isDatabaseMaintenanceInProgress;
        private set
        {
            if (SetField(ref _isDatabaseMaintenanceInProgress, value) &&
                RunDatabaseMaintenanceCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
            }
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
