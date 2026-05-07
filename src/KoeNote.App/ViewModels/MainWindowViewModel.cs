using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Presets;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.SystemStatus;
using KoeNote.App.Services.Transcript;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.ViewModels;

public sealed record AsrEngineOption(string EngineId, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private const string DefaultSelectableAsrEngineId = "faster-whisper-large-v3-turbo";
    private const double MinMainContentZoomScale = 0.9;
    private const double MaxMainContentZoomScale = 1.5;
    private static readonly double[] MainContentZoomSteps = [0.9, 1.0, 1.1, 1.25, 1.5];

    private readonly JobRepository _jobRepository;
    private readonly JobLogRepository _jobLogRepository;
    private readonly AsrSettingsRepository _asrSettingsRepository;
    private readonly TranscriptSegmentRepository _transcriptSegmentRepository;
    private readonly TranscriptDerivativeRepository _transcriptDerivativeRepository;
    private readonly AsrEngineRegistry _asrEngineRegistry;
    private readonly JobRunCoordinator _jobRunCoordinator;
    private readonly CorrectionDraftRepository _correctionDraftRepository;
    private readonly ReviewOperationService _reviewOperationService;
    private readonly TranscriptEditService _transcriptEditService;
    private readonly CorrectionMemoryService _correctionMemoryService;
    private readonly TranscriptExportService _transcriptExportService;
    private readonly ModelCatalogService _modelCatalogService;
    private readonly InstalledModelRepository _installedModelRepository;
    private readonly ModelDownloadJobRepository _modelDownloadJobRepository;
    private readonly ModelInstallService _modelInstallService;
    private readonly ModelDownloadService _modelDownloadService;
    private readonly ModelLicenseViewer _modelLicenseViewer;
    private readonly SetupWizardService _setupWizardService;
    private readonly IAudioPlaybackService _audioPlaybackService;
    private readonly TextDiffService _textDiffService = new();
    private readonly StatusBarInfoService _statusBarInfoService;
    private readonly DatabaseMaintenanceService _databaseMaintenanceService;
    private readonly DomainPresetImportService _domainPresetImportService;
    private readonly IUpdateCheckService _updateCheckService;
    private readonly IUpdateDownloadService _updateDownloadService;
    private readonly IUpdateInstallerLauncher _updateInstallerLauncher;
    private readonly IUpdateHistoryService _updateHistoryService;
    private readonly UiPreferencesService _uiPreferencesService;
    private readonly Action _shutdownApplication;
    private readonly DispatcherTimer _statusRefreshTimer;
    private readonly DispatcherTimer _playbackRefreshTimer;
    private readonly DispatcherTimer _modelDownloadNotificationTimer;
    private StatusBarInfo _statusBarInfo;
    private bool _isStatusRefreshInProgress;
    private bool _isRefreshingPlaybackPosition;
    private bool _isSelectingSegmentForPlayback;
    private JobSummary? _selectedJob;
    private TranscriptSegmentPreview? _selectedSegment;
    private CorrectionDraft? _selectedCorrectionDraft;
    private DomainPresetImportHistoryItem? _selectedDomainPresetImport;
    private string? _loadedDomainPresetPath;
    private string _loadedDomainPresetSummary = "プリセットJSONは未読み込みです。";
    private string _loadedDomainPresetDetails = string.Empty;
    private ModelCatalogEntry? _selectedModelCatalogEntry;
    private SetupPresetRecommendation? _setupPresetRecommendation;
    private ModelQualityPreset? _selectedSetupModelPreset;
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
    private bool _isTranscriptAutoScrollEnabled;
    private bool _rememberCorrection = true;
    private double _playbackPositionSeconds;
    private double _playbackDurationSeconds;
    private double _playbackRate = 1.0;
    private double _playbackVolume = 1.0;
    private string _exportWarning = string.Empty;
    private string _lastExportFolder = string.Empty;
    private bool _includeExportTimestamps = true;
    private string _summaryContent = string.Empty;
    private string _summaryStatus = "要約はまだありません。";
    private string _latestLog;
    private string _modelDownloadProgressSummary = "No active model download.";
    private string _modelDownloadNotification = string.Empty;
    private bool _isModelDownloadNotificationError;
    private string _setupDiarizationRuntimeSummary = "話者識別ランタイムは未導入です。必要になったらここから追加できます。";
    private string _databaseMaintenanceSummary = string.Empty;
    private string _updateNotificationTitle = string.Empty;
    private string _updateNotificationMessage = string.Empty;
    private string _updateDownloadProgressText = string.Empty;
    private string _verifiedUpdateInstallerPath = string.Empty;
    private LatestReleaseInfo? _availableUpdate;
    private bool _isDatabaseMaintenanceInProgress;
    private bool _isUpdateCheckInProgress;
    private bool _isUpdateDownloadInProgress;
    private double _modelDownloadProgressPercent;
    private bool _isModelDownloadInProgress;
    private bool _isModelDownloadProgressIndeterminate;
    private DateTimeOffset _lastModelCatalogProgressRefreshAt = DateTimeOffset.MinValue;
    private string? _lastModelCatalogProgressRefreshModelId;
    private int _lastModelCatalogProgressRefreshPercent = -1;
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
    private string _selectedAsrEngineId = "faster-whisper-large-v3-turbo";
    private bool _enableReviewStage = true;
    private bool _enableSummaryStage;
    private string _selectedSegmentEditText = string.Empty;
    private string _selectedSpeakerAlias = string.Empty;
    private double _mainContentZoomScale = 1.0;
    private bool _isSegmentInlineEditActive;
    private bool _isSpeakerInlineEditActive;
    private bool _isReloadingSegments;
    private bool _isRunInProgress;
    private bool _isSummaryStageRunning;
    private string _reviewIssueType = "候補なし";
    private string _originalText = string.Empty;
    private string _suggestedText = string.Empty;
    private string _reviewReason = "整文候補はありません。";
    private double _confidence;
    private int _reviewSegmentFocusRequestId;
    private int _transcriptAutoScrollRequestId;

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
        _modelDownloadJobRepository = services.ModelDownloadJobRepository;
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
        _domainPresetImportService = services.DomainPresetImportService;
        _updateCheckService = services.UpdateCheckService;
        _updateDownloadService = services.UpdateDownloadService;
        _updateInstallerLauncher = services.UpdateInstallerLauncher;
        _updateHistoryService = services.UpdateHistoryService;
        _transcriptDerivativeRepository = services.TranscriptDerivativeRepository;
        _uiPreferencesService = new UiPreferencesService(paths);
        var currentApplication = System.Windows.Application.Current;
        _shutdownApplication = currentApplication is null ? (() => { }) : currentApplication.Shutdown;
        _mainContentZoomScale = NormalizeZoomScale(_uiPreferencesService.Load().MainContentZoomScale);
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
        _enableSummaryStage = asrSettings.EnableSummaryStage;
        RefreshOptionalStageToggleStatuses();
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
        SkipToPreviousSegmentCommand = new RelayCommand(SkipToPreviousSegmentAsync, CanSkipPlaybackSegment);
        SkipToNextSegmentCommand = new RelayCommand(SkipToNextSegmentAsync, CanSkipPlaybackSegment);
        OpenSettingsCommand = new RelayCommand(OpenSettingsAsync);
        OpenLogsCommand = new RelayCommand(OpenLogsAsync);
        OpenCleanupToolCommand = new RelayCommand(OpenCleanupToolAsync, CanOpenCleanupTool);
        ImportDomainPresetCommand = new RelayCommand(ImportDomainPresetAsync, () => !IsRunInProgress);
        ApplyLoadedDomainPresetCommand = new RelayCommand(ApplyLoadedDomainPresetAsync, () => !IsRunInProgress && HasLoadedDomainPreset);
        ClearDomainPresetCommand = new RelayCommand(ClearSelectedDomainPresetAsync, () => SelectedDomainPresetImport?.DeactivatedAt is null && !IsRunInProgress);
        OpenSetupCommand = new RelayCommand(OpenSetupAsync);
        CloseSetupWizardModalCommand = new RelayCommand(CloseSetupWizardModalAsync);
        OpenSelectedDetailPanelCommand = new RelayCommand(OpenSelectedDetailPanelAsync, CanOpenSelectedDetailPanel);
        CloseDetailPanelCommand = new RelayCommand(CloseDetailPanelAsync);
        SetupBackCommand = new RelayCommand(SetupBackAsync);
        SetupNextCommand = new RelayCommand(SetupNextAsync);
        SetupUseRecommendedCommand = new RelayCommand(SetupUseRecommendedAsync);
        SetupAcceptLicensesCommand = new RelayCommand(SetupAcceptLicensesAsync);
        SetupInstallSelectedPresetCommand = new RelayCommand(SetupInstallSelectedPresetAsync, CanInstallSelectedPreset);
        SetupDownloadAsrCommand = new RelayCommand(SetupDownloadAsrAsync, CanDownloadSetupAsr);
        SetupDownloadReviewCommand = new RelayCommand(SetupDownloadReviewAsync, CanDownloadSetupReview);
        SetupInstallDiarizationRuntimeCommand = new RelayCommand(SetupInstallDiarizationRuntimeAsync, () => !IsModelDownloadInProgress);
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
        DeleteSelectedModelFilesCommand = new RelayCommand(DeleteSelectedModelFilesAsync, CanDeleteSelectedModelFiles);
        AcceptDraftCommand = new RelayCommand(AcceptSelectedDraftAsync, CanOperateOnSelectedDraft);
        RejectDraftCommand = new RelayCommand(RejectSelectedDraftAsync, CanOperateOnSelectedDraft);
        ApplyManualEditCommand = new RelayCommand(ApplyManualEditAsync, CanOperateOnSelectedDraft);
        SelectPreviousDraftCommand = new RelayCommand(SelectPreviousDraftAsync, CanSelectPreviousDraft);
        SelectNextDraftCommand = new RelayCommand(SelectNextDraftAsync, CanSelectNextDraft);
        FocusSelectedDraftSegmentCommand = new RelayCommand(FocusSelectedDraftSegmentAsync, () => SelectedCorrectionDraft is not null);
        SaveSegmentEditCommand = new RelayCommand(SaveSegmentEditAsync, CanEditSelectedSegment);
        BeginSegmentInlineEditCommand = new RelayCommand<TranscriptSegmentPreview>(BeginSegmentInlineEditAsync, CanBeginSegmentInlineEdit);
        SaveSegmentInlineEditCommand = new RelayCommand(SaveSegmentInlineEditAsync, CanEditSelectedSegment);
        CancelSegmentInlineEditCommand = new RelayCommand(CancelSegmentInlineEditAsync, () => IsSegmentInlineEditActive);
        RevertSegmentEditCommand = new RelayCommand<TranscriptSegmentPreview>(RevertSegmentEditAsync, CanRevertSegmentEdit);
        BeginSpeakerInlineEditCommand = new RelayCommand<TranscriptSegmentPreview>(BeginSpeakerInlineEditAsync, CanBeginSpeakerInlineEdit);
        SaveSpeakerInlineEditCommand = new RelayCommand(SaveSpeakerInlineEditAsync, CanEditSelectedSpeaker);
        SaveSpeakerAliasCommand = new RelayCommand(SaveSpeakerAliasAsync, CanEditSelectedSpeaker);
        UndoLastOperationCommand = new RelayCommand(UndoLastOperationAsync);
        ExportSelectedJobCommand = new RelayCommand(ExportSelectedJobAsync, CanExportSelectedJob);
        ExportTxtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Text), CanExportSelectedJob);
        ExportJsonCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Json), CanExportSelectedJob);
        ExportSrtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Srt), CanExportSelectedJob);
        ExportDocxCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Docx), CanExportSelectedJob);
        ExportRawTxtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Text, TranscriptExportSource.Raw), CanExportSelectedJob);
        ExportRawMarkdownCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Markdown, TranscriptExportSource.Raw), CanExportSelectedJob);
        ExportRawJsonCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Json, TranscriptExportSource.Raw), CanExportSelectedJob);
        ExportRawSrtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Srt, TranscriptExportSource.Raw), CanExportSelectedJob);
        ExportRawVttCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Vtt, TranscriptExportSource.Raw), CanExportSelectedJob);
        ExportRawDocxCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Docx, TranscriptExportSource.Raw), CanExportSelectedJob);
        ExportPolishedTxtCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Text, TranscriptExportSource.Polished), CanExportSelectedJob);
        ExportPolishedMarkdownCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Markdown, TranscriptExportSource.Polished), CanExportSelectedJob);
        ExportPolishedDocxCommand = new RelayCommand(() => ExportSelectedJobFormatAsync(TranscriptExportFormat.Docx, TranscriptExportSource.Polished), CanExportSelectedJob);
        ExportSummaryMarkdownCommand = new RelayCommand(ExportSummaryMarkdownAsync, CanExportSummaryMarkdown);
        ExportSummaryTextCommand = new RelayCommand(ExportSummaryTextAsync, CanExportSummaryMarkdown);
        OpenExportFolderCommand = new RelayCommand(OpenExportFolderAsync, CanOpenExportFolder);
        ZoomOutCommand = new RelayCommand(ZoomOutAsync, CanZoomOut);
        ZoomInCommand = new RelayCommand(ZoomInAsync, CanZoomIn);
        ResetZoomCommand = new RelayCommand(ResetZoomAsync, () => Math.Abs(MainContentZoomScale - 1.0) > 0.001);
        RunDatabaseMaintenanceCommand = new RelayCommand(RunDatabaseMaintenanceAsync, CanRunDatabaseMaintenance);
        CheckForUpdatesCommand = new RelayCommand(CheckForUpdatesAsync, () => !IsUpdateCheckInProgress);
        DownloadUpdateCommand = new RelayCommand(DownloadUpdateAsync, CanDownloadUpdate);
        InstallVerifiedUpdateCommand = new RelayCommand(InstallVerifiedUpdateAsync, CanInstallVerifiedUpdate);
        DismissUpdateNotificationCommand = new RelayCommand(DismissUpdateNotificationAsync);
        OpenUpdateReleaseNotesCommand = new RelayCommand(OpenUpdateReleaseNotesAsync, () => AvailableUpdateReleaseNotesUrl is not null);

        MarkInterruptedModelDownloads();
        RegisterDiscoveredManagedModels();
        RefreshModelCatalog();
        RefreshSetupWizard();
        IsSetupWizardModalOpen = !IsSetupComplete;
        RefreshSpeakerFilters();
        RefreshDatabaseMaintenanceSummary();
        RefreshDomainPresetHistory();
        _ = CheckForUpdatesOnStartupAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppPaths Paths { get; }

    public Action<Uri> OpenUriAction { get; set; } = OpenUriInShell;

    public string AppVersionDisplay => $"v{GetAppVersion()}";

    public string AppVersionToolTip => $"KoeNote {GetAppInformationalVersion()}";

    public string GpuSummary => EnvironmentStatus.FirstOrDefault(item => item.Name == "nvidia-smi")?.Detail ?? "Unknown";

    public string AsrModel => FindInstalledCatalogEntry(
        "asr",
        entry => string.Equals(entry.EngineId, SelectedAsrEngineId, StringComparison.OrdinalIgnoreCase))
        ?.DisplayName ?? "未設定";

    public string ReviewModel => FindInstalledCatalogEntry(
        "review",
        entry => string.Equals(entry.ModelId, _setupState.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase))
        ?.DisplayName ?? "未設定";

    public string StoragePath => Paths.Root;

    public string DiskFreeSummary => _statusBarInfo.DiskFreeSummary;

    public string MemorySummary => _statusBarInfo.MemorySummary;

    public string CpuSummary => _statusBarInfo.CpuSummary;

    public string GpuUsageSummary => _statusBarInfo.GpuUsageSummary;

    public string FirstRunSummary
    {
        get
        {
            var missingCount = GetBlockingEnvironmentIssues().Count();
            return missingCount == 0
                ? "初回チェック OK"
                : $"初回チェック: {missingCount} 件の確認が必要";
        }
    }

    public string FirstRunDetail
    {
        get
        {
            var missingItems = GetBlockingEnvironmentIssues().ToArray();
            return missingItems.Length == 0
                ? "必要なランタイム、ツール、モデルを確認済みです。"
                : string.Join(Environment.NewLine, missingItems.Select(static item => $"{item.Name}: {item.Detail}"))
                    + Environment.NewLine
                    + "セットアップ、またはモデル導入から不足項目を確認してください。";
        }
    }

    public string SetupPlaceholderText =>
        "セットアップウィザードで ASR / 整文モデルの導入を案内します。必要なモデルやランタイムの状態を確認できます。";

    private IEnumerable<StatusItem> GetBlockingEnvironmentIssues()
    {
        return EnvironmentStatus.Where(IsBlockingEnvironmentIssue);
    }

    private bool IsBlockingEnvironmentIssue(StatusItem item)
    {
        if (item.IsOk)
        {
            return false;
        }

        return item.Name switch
        {
            "ffmpeg" => true,
            "AppData" or "SQLite" => true,
            _ => false
        };
    }

    private IReadOnlyList<string> GetRunPreflightIssues()
    {
        var issues = new List<string>();
        if (SelectedJob is null)
        {
            issues.Add("ジョブを選択してください。");
        }
        else if (!File.Exists(SelectedJob.SourceAudioPath))
        {
            issues.Add($"音声ファイルが見つかりません: {SelectedJob.SourceAudioPath}");
        }

        if (!IsSetupComplete)
        {
            issues.Add("セットアップを完了してください。");
        }

        if (!File.Exists(Paths.FfmpegPath))
        {
            issues.Add($"ffmpeg が見つかりません: {Paths.FfmpegPath}");
        }

        if (!IsSelectedAsrEngineReady())
        {
            issues.Add($"ASR モデルまたは実行スクリプトが不足しています: {SelectedAsrEngineId}");
        }

        if (EnableReviewStage && !ReviewStageAssetsReady)
        {
            issues.Add($"整文ランタイムまたは整文モデルが不足しています: {GetSelectedReviewRuntimePath()}");
        }

        if (EnableSummaryStage && !SummaryStageAssetsReady)
        {
            issues.Add($"要約ランタイムまたは要約モデルが不足しています: {GetSelectedReviewRuntimePath()}");
        }

        return issues;
    }

    public string SetupDiarizationRuntimeSummary
    {
        get => _setupDiarizationRuntimeSummary;
        private set => SetField(ref _setupDiarizationRuntimeSummary, value);
    }

    public bool RequiredRuntimeAssetsReady => EnvironmentStatus
        .Where(item => item.Name == "ffmpeg")
        .All(static item => item.IsOk) &&
        IsSelectedAsrEngineReady();

    public bool ReviewStageAssetsReady => EnvironmentStatus
        .Where(static item => item.Name == "llama-completion")
        .All(static item => item.IsOk) &&
        IsSelectedReviewRuntimeReady() &&
        IsReviewModelReady();

    public bool SummaryStageAssetsReady => ReviewStageAssetsReady;

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
        SetupStep.SetupMode => "モデル構成を選びます",
        SetupStep.AsrModel => "文字起こしモデルを選びます",
        SetupStep.ReviewModel => "整文モデルを選びます",
        SetupStep.Storage => "モデルの保存先を確認します",
        SetupStep.License => "ライセンスを確認します",
        SetupStep.Install => "モデルを導入します",
        SetupStep.SmokeTest => "最後に動作確認します",
        SetupStep.Complete => "準備完了です",
        _ => "初回セットアップ"
    };

    public string SetupWizardModalGuide => _setupState.CurrentStep switch
    {
        SetupStep.Welcome => "KoeNote は本体だけ先に起動し、ASR / 整文モデルはあとから導入します。ここでは最初の文字起こしに必要な準備を順番に案内します。",
        SetupStep.EnvironmentCheck => "足りない runtime やモデルがあってもアプリ本体は壊れません。ここで次に必要な導入操作を確認できます。",
        SetupStep.SetupMode => "軽量、推奨、高精度、実験的からPC環境に合う構成を選びます。あとからASRと整文モデルを個別に変更できます。",
        SetupStep.AsrModel => "日本語文字起こしには faster-whisper large-v3-turbo を推奨します。精度優先なら large-v3 も選べます。",
        SetupStep.ReviewModel => "整文は文字起こし結果を読みやすく整える追加ステージです。不要な場合は Settings でいつでもスキップできます。",
        SetupStep.Storage => "オンラインダウンロード、ローカルファイル、offline model pack のどれでも導入できます。",
        SetupStep.License => "モデルごとの license / size / runtime requirement を確認してから導入します。",
        SetupStep.Install => "ダウンロード中は進捗を表示します。失敗しても本体は起動したままで、別経路から再試行できます。",
        SetupStep.SmokeTest => "ネットワークなしで startup、sample import、review screen、export path を確認します。",
        SetupStep.Complete => "セットアップが完了すると Run が有効になります。通常画面からいつでも Setup / Models を開けます。",
        _ => "KoeNote の初回利用に必要な準備を案内します。"
    };

    public bool CanRunSelectedJob => !IsRunInProgress && GetRunPreflightIssues().Count == 0;

    public string RunPreflightSummary
    {
        get
        {
            var issues = GetRunPreflightIssues().ToArray();
            return issues.Length == 0
                ? "実行できます"
                : $"実行前チェック: {issues.Length} 件の確認が必要";
        }
    }

    public string RunPreflightDetail
    {
        get
        {
            var issues = GetRunPreflightIssues().ToArray();
            var details = issues.Length == 0
                ? "選択ジョブ、セットアップ、ASR 実行環境を確認済みです。"
                : string.Join(Environment.NewLine, issues);
            return details.Trim();
        }
    }

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

    public ObservableCollection<ModelQualityPreset> SetupModelPresetChoices { get; } = [];

    public ObservableCollection<SetupSmokeCheck> SetupSmokeChecks { get; } = [];

    public ObservableCollection<SetupModelAudit> SetupModelAudits { get; } = [];

    public ObservableCollection<SetupExistingDataItem> SetupExistingData { get; } = [];

    public ObservableCollection<DomainPresetImportHistoryItem> DomainPresetImports { get; } = [];

    public string LoadedDomainPresetSummary
    {
        get => _loadedDomainPresetSummary;
        private set => SetField(ref _loadedDomainPresetSummary, value);
    }

    public string LoadedDomainPresetDetails
    {
        get => _loadedDomainPresetDetails;
        private set => SetField(ref _loadedDomainPresetDetails, value);
    }

    public bool HasLoadedDomainPreset => !string.IsNullOrWhiteSpace(_loadedDomainPresetPath);

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

    public ICommand SkipToPreviousSegmentCommand { get; }

    public ICommand SkipToNextSegmentCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand OpenLogsCommand { get; }

    public ICommand OpenCleanupToolCommand { get; }

    public ICommand ImportDomainPresetCommand { get; }

    public ICommand ApplyLoadedDomainPresetCommand { get; }

    public ICommand ClearDomainPresetCommand { get; }

    public ICommand OpenSetupCommand { get; }

    public ICommand CloseSetupWizardModalCommand { get; }

    public ICommand OpenSelectedDetailPanelCommand { get; }

    public ICommand CloseDetailPanelCommand { get; }

    public ICommand SetupBackCommand { get; }

    public ICommand SetupNextCommand { get; }

    public ICommand SetupUseRecommendedCommand { get; }

    public ICommand SetupAcceptLicensesCommand { get; }

    public ICommand SetupInstallSelectedPresetCommand { get; }

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

    public ICommand DeleteSelectedModelFilesCommand { get; }

    public ICommand AcceptDraftCommand { get; }

    public ICommand RejectDraftCommand { get; }

    public ICommand ApplyManualEditCommand { get; }

    public ICommand SelectPreviousDraftCommand { get; }

    public ICommand SelectNextDraftCommand { get; }

    public ICommand FocusSelectedDraftSegmentCommand { get; }

    public ICommand SaveSegmentEditCommand { get; }

    public ICommand BeginSegmentInlineEditCommand { get; }

    public ICommand SaveSegmentInlineEditCommand { get; }

    public ICommand CancelSegmentInlineEditCommand { get; }

    public ICommand RevertSegmentEditCommand { get; }

    public ICommand BeginSpeakerInlineEditCommand { get; }

    public ICommand SaveSpeakerInlineEditCommand { get; }

    public ICommand SaveSpeakerAliasCommand { get; }

    public ICommand UndoLastOperationCommand { get; }

    public ICommand ExportSelectedJobCommand { get; }

    public ICommand ExportTxtCommand { get; }

    public ICommand ExportJsonCommand { get; }

    public ICommand ExportSrtCommand { get; }

    public ICommand ExportDocxCommand { get; }

    public ICommand ExportRawTxtCommand { get; }

    public ICommand ExportRawMarkdownCommand { get; }

    public ICommand ExportRawJsonCommand { get; }

    public ICommand ExportRawSrtCommand { get; }

    public ICommand ExportRawVttCommand { get; }

    public ICommand ExportRawDocxCommand { get; }

    public ICommand ExportPolishedTxtCommand { get; }

    public ICommand ExportPolishedMarkdownCommand { get; }

    public ICommand ExportPolishedDocxCommand { get; }

    public ICommand ExportSummaryMarkdownCommand { get; }

    public ICommand ExportSummaryTextCommand { get; }

    public ICommand OpenExportFolderCommand { get; }

    public ICommand ZoomOutCommand { get; }

    public ICommand ZoomInCommand { get; }

    public ICommand ResetZoomCommand { get; }

    public ICommand RunDatabaseMaintenanceCommand { get; }

    public ICommand CheckForUpdatesCommand { get; }

    public ICommand DownloadUpdateCommand { get; }

    public ICommand InstallVerifiedUpdateCommand { get; }

    public ICommand DismissUpdateNotificationCommand { get; }

    public ICommand OpenUpdateReleaseNotesCommand { get; }

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
                OnPropertyChanged(nameof(RunPreflightSummary));
                OnPropertyChanged(nameof(RunPreflightDetail));
                RefreshPlaybackWaveform();
                StopAudioPlayback();
                RefreshLogs();
                ReloadSegmentsForSelectedJob();
                LoadSummaryForSelectedJob();
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
            if (!_isReloadingSegments &&
                _isSegmentInlineEditActive &&
                _selectedSegment is { } previousSegment &&
                value is not null &&
                !string.Equals(previousSegment.SegmentId, value.SegmentId, StringComparison.Ordinal))
            {
                if (!CommitSegmentInlineEdit(previousSegment, reloadSegments: false))
                {
                    return;
                }
            }

            if (!_isReloadingSegments &&
                _isSpeakerInlineEditActive &&
                _selectedSegment is { } previousSpeakerSegment &&
                value is not null &&
                !string.Equals(previousSpeakerSegment.SegmentId, value.SegmentId, StringComparison.Ordinal))
            {
                if (!CommitSpeakerInlineEdit(previousSpeakerSegment, reloadSegments: false))
                {
                    return;
                }
            }

            if (!SetField(ref _selectedSegment, value))
            {
                return;
            }

            if (value is null)
            {
                IsSegmentInlineEditActive = false;
                IsSpeakerInlineEditActive = false;
                SelectedSegmentEditText = string.Empty;
                SelectedSpeakerAlias = string.Empty;
                UpdateSegmentEditCommandStates();
            }
            else
            {
                SelectedSegmentEditText = value.Text;
                SelectedSpeakerAlias = value.Speaker;
                if (!_isSelectingSegmentForDraft && !_isSelectingSegmentForPlayback)
                {
                    SelectFirstDraftForSegment(value.SegmentId);
                }

                UpdateSegmentEditCommandStates();
                if (!_isSelectingSegmentForPlayback)
                {
                    SeekPlaybackToSelectedSegment(value);
                }
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

    public DomainPresetImportHistoryItem? SelectedDomainPresetImport
    {
        get => _selectedDomainPresetImport;
        set
        {
            if (SetField(ref _selectedDomainPresetImport, value) &&
                ClearDomainPresetCommand is RelayCommand command)
            {
                command.RaiseCanExecuteChanged();
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

    public ModelQualityPreset? SelectedSetupModelPreset
    {
        get => _selectedSetupModelPreset;
        set
        {
            if (SetField(ref _selectedSetupModelPreset, value) && value is not null)
            {
                ApplySetupModelPreset(value.PresetId);
            }
        }
    }

    public string SelectedSetupModelPresetDescription => SelectedSetupModelPreset?.Description ?? string.Empty;

    public string SelectedSetupModelPresetModels => SelectedSetupModelPreset is null
        ? string.Empty
        : $"ASR: {FindSetupModelDisplayName(SetupAsrModelChoices, SelectedSetupModelPreset.AsrModelId)} / Review: {FindSetupModelDisplayName(SetupReviewModelChoices, SelectedSetupModelPreset.ReviewModelId)}";

    public bool SelectedSetupModelsReady => IsSetupModelReady(SelectedSetupAsrModel) &&
        IsSetupModelReady(SelectedSetupReviewModel);

    public bool SetupFasterWhisperRuntimeReady => FasterWhisperRuntimeLayout.HasPackage(Paths);

    public bool SetupDiarizationRuntimeReady => DiarizationRuntimeLayout.HasPackage(Paths);

    public bool SelectedSetupConfigurationReady => SelectedSetupModelsReady && SetupFasterWhisperRuntimeReady && SetupDiarizationRuntimeReady;

    public string SetupPrimaryInstallActionText => IsModelDownloadInProgress
        ? "導入中..."
        : SelectedSetupConfigurationReady
            ? "構成は導入済み"
            : "構成をまとめて導入";

    public string SetupPrimaryInstallSummary
    {
        get
        {
            if (IsModelDownloadInProgress)
            {
                return "選択した構成の導入を進めています。このまま完了を待ってください。";
            }

            if (SelectedSetupConfigurationReady)
            {
                return "ASR、整文モデル、話者識別ランタイムは導入済みです。ライセンス同意と最終確認へ進めます。";
            }

            var missing = new List<string>();
            if (!IsSetupModelReady(SelectedSetupAsrModel))
            {
                missing.Add("ASR");
            }

            if (!IsSetupModelReady(SelectedSetupReviewModel))
            {
                missing.Add("整文モデル");
            }

            if (!SetupFasterWhisperRuntimeReady)
            {
                missing.Add("ASR runtime");
            }

            if (!SetupDiarizationRuntimeReady)
            {
                missing.Add("話者識別ランタイム");
            }

            return missing.Count == 0
                ? "選択した構成を確認しています。"
                : $"{string.Join(" / ", missing)} をまとめて導入できます。";
        }
    }

    public string SetupPresetRecommendationSummary => _setupPresetRecommendation is null
        ? string.Empty
        : $"自動判定のおすすめ: {_setupPresetRecommendation.DisplayName}";

    public string SetupPresetRecommendationDetail => _setupPresetRecommendation?.Detail ?? string.Empty;

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
        SetupStep.SetupMode => "モデル構成",
        SetupStep.AsrModel => "ASRモデル",
        SetupStep.ReviewModel => "整文LLM",
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

    private static bool IsSetupModelReady(ModelCatalogEntry? model)
    {
        return model?.InstalledModel is { Verified: true } installed &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath));
    }

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
        private set
        {
            if (SetField(ref _isModelDownloadInProgress, value))
            {
                UpdateModelCatalogCommandStates();
                UpdateSetupDownloadCommandStates();
                OnPropertyChanged(nameof(SelectedSetupModelsReady));
                OnPropertyChanged(nameof(SetupFasterWhisperRuntimeReady));
                OnPropertyChanged(nameof(SetupDiarizationRuntimeReady));
                OnPropertyChanged(nameof(SelectedSetupConfigurationReady));
                OnPropertyChanged(nameof(SetupPrimaryInstallActionText));
                OnPropertyChanged(nameof(SetupPrimaryInstallSummary));
            }
        }
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

    public int TranscriptAutoScrollRequestId
    {
        get => _transcriptAutoScrollRequestId;
        private set => SetField(ref _transcriptAutoScrollRequestId, value);
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
            if (SetField(ref _selectedDetailPanelTabIndex, Math.Clamp(value, 0, 3)))
            {
                OnPropertyChanged(nameof(DetailPanelTitle));
                SelectedLogPanelTabIndex = _selectedDetailPanelTabIndex;
            }
        }
    }

    public bool IsSegmentInlineEditActive
    {
        get => _isSegmentInlineEditActive;
        private set
        {
            if (SetField(ref _isSegmentInlineEditActive, value))
            {
                UpdateSegmentEditCommandStates();
            }
        }
    }

    public bool IsSpeakerInlineEditActive
    {
        get => _isSpeakerInlineEditActive;
        private set
        {
            if (SetField(ref _isSpeakerInlineEditActive, value))
            {
                UpdateSegmentEditCommandStates();
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
        3 => "ログ",
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
                RefreshOptionalStageToggleStatuses();
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

                if (InstallVerifiedUpdateCommand is RelayCommand installUpdateCommand)
                {
                    installUpdateCommand.RaiseCanExecuteChanged();
                }

                UpdateModelCatalogCommandStates();
                OnPropertyChanged(nameof(RunPreflightSummary));
                OnPropertyChanged(nameof(RunPreflightDetail));

                if (OpenCleanupToolCommand is RelayCommand cleanupCommand)
                {
                    cleanupCommand.RaiseCanExecuteChanged();
                }

                if (ImportDomainPresetCommand is RelayCommand presetCommand)
                {
                    presetCommand.RaiseCanExecuteChanged();
                }

                if (ApplyLoadedDomainPresetCommand is RelayCommand applyPresetCommand)
                {
                    applyPresetCommand.RaiseCanExecuteChanged();
                }

                if (ClearDomainPresetCommand is RelayCommand clearPresetCommand)
                {
                    clearPresetCommand.RaiseCanExecuteChanged();
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

    public bool IsTranscriptAutoScrollEnabled
    {
        get => _isTranscriptAutoScrollEnabled;
        set
        {
            if (SetField(ref _isTranscriptAutoScrollEnabled, value) && value)
            {
                SelectSegmentForPlaybackPosition(PlaybackPositionSeconds);
            }
        }
    }

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
            SelectSegmentForPlaybackPosition(nextValue);

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

    public string SummaryContent
    {
        get => _summaryContent;
        private set
        {
            if (SetField(ref _summaryContent, value))
            {
                OnPropertyChanged(nameof(HasSummaryContent));
                OnPropertyChanged(nameof(SummaryContentDisplay));
                UpdateExportCommandStates();
            }
        }
    }

    public bool HasSummaryContent => !string.IsNullOrWhiteSpace(SummaryContent);

    public string SummaryContentDisplay => HasSummaryContent
        ? SummaryContent
        : "要約はまだ生成されていません。";

    public string SummaryStatus
    {
        get => _summaryStatus;
        private set => SetField(ref _summaryStatus, value);
    }

    public double MainContentZoomScale
    {
        get => _mainContentZoomScale;
        private set
        {
            var normalized = NormalizeZoomScale(value);
            if (Math.Abs(_mainContentZoomScale - normalized) < 0.001)
            {
                return;
            }

            _mainContentZoomScale = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MainContentZoomPercentText));
            OnPropertyChanged(nameof(MainContentZoomToolTip));
            UpdateZoomCommandStates();
            _uiPreferencesService.Save(new UiPreferences(_mainContentZoomScale));
        }
    }

    public string MainContentZoomPercentText => $"{MainContentZoomScale * 100:0}%";

    public string MainContentZoomToolTip => $"表示倍率 {MainContentZoomPercentText}";

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
                OnPropertyChanged(nameof(ReviewStageAssetsReady));
                OnPropertyChanged(nameof(CanRunSelectedJob));
                OnPropertyChanged(nameof(RunPreflightSummary));
                OnPropertyChanged(nameof(RunPreflightDetail));
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
                OnPropertyChanged(nameof(ReviewStageAssetsReady));
                OnPropertyChanged(nameof(CanRunSelectedJob));
                OnPropertyChanged(nameof(RunPreflightSummary));
                OnPropertyChanged(nameof(RunPreflightDetail));
                OnPropertyChanged(nameof(FirstRunSummary));
                OnPropertyChanged(nameof(FirstRunDetail));
                OnPropertyChanged(nameof(ReviewStageToggleText));
                OnPropertyChanged(nameof(ReviewStageToggleToolTip));
                RefreshOptionalStageToggleStatuses();
                if (RunSelectedJobCommand is RelayCommand runCommand)
                {
                    runCommand.RaiseCanExecuteChanged();
                }

                ScheduleSaveAsrSettings();
            }
        }
    }

    public string ReviewStageToggleText => EnableReviewStage ? "整文 ON" : "整文 OFF";

    public string ReviewStageToggleToolTip => EnableReviewStage
        ? "整文ステージを実行します。クリックでスキップ"
        : "整文ステージをスキップします。クリックで実行";

    public bool IsSummaryStageRunning
    {
        get => _isSummaryStageRunning;
        private set => SetField(ref _isSummaryStageRunning, value);
    }

    public bool EnableSummaryStage
    {
        get => _enableSummaryStage;
        set
        {
            if (SetField(ref _enableSummaryStage, value))
            {
                OnPropertyChanged(nameof(RequiredRuntimeAssetsReady));
                OnPropertyChanged(nameof(SummaryStageAssetsReady));
                OnPropertyChanged(nameof(CanRunSelectedJob));
                OnPropertyChanged(nameof(RunPreflightSummary));
                OnPropertyChanged(nameof(RunPreflightDetail));
                OnPropertyChanged(nameof(FirstRunSummary));
                OnPropertyChanged(nameof(FirstRunDetail));
                OnPropertyChanged(nameof(SummaryStageToggleText));
                OnPropertyChanged(nameof(SummaryStageToggleToolTip));
                RefreshOptionalStageToggleStatuses();
                if (RunSelectedJobCommand is RelayCommand runCommand)
                {
                    runCommand.RaiseCanExecuteChanged();
                }

                ScheduleSaveAsrSettings();
            }
        }
    }

    public string SummaryStageToggleText => EnableSummaryStage ? "要約 ON" : "要約 OFF";

    public string SummaryStageToggleToolTip => EnableSummaryStage
        ? "要約ステージを実行します。クリックでスキップ"
        : "要約ステージをスキップします。クリックで実行";

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

    private static string GetAppVersion()
    {
        var informationalVersion = GetAppInformationalVersion();
        var metadataIndex = informationalVersion.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            informationalVersion = informationalVersion[..metadataIndex];
        }

        return string.IsNullOrWhiteSpace(informationalVersion)
            ? "0.0.0"
            : informationalVersion;
    }

    private static string GetAppInformationalVersion()
    {
        var assembly = typeof(MainWindowViewModel).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    private Task ZoomOutAsync()
    {
        MainContentZoomScale = GetPreviousZoomStep(MainContentZoomScale);
        return Task.CompletedTask;
    }

    private Task ZoomInAsync()
    {
        MainContentZoomScale = GetNextZoomStep(MainContentZoomScale);
        return Task.CompletedTask;
    }

    private Task ResetZoomAsync()
    {
        MainContentZoomScale = 1.0;
        return Task.CompletedTask;
    }

    private bool CanZoomOut()
    {
        return MainContentZoomScale > MinMainContentZoomScale + 0.001;
    }

    private bool CanZoomIn()
    {
        return MainContentZoomScale < MaxMainContentZoomScale - 0.001;
    }

    private void UpdateZoomCommandStates()
    {
        if (ZoomOutCommand is RelayCommand zoomOutCommand)
        {
            zoomOutCommand.RaiseCanExecuteChanged();
        }

        if (ZoomInCommand is RelayCommand zoomInCommand)
        {
            zoomInCommand.RaiseCanExecuteChanged();
        }

        if (ResetZoomCommand is RelayCommand resetCommand)
        {
            resetCommand.RaiseCanExecuteChanged();
        }
    }

    private static double GetPreviousZoomStep(double current)
    {
        var normalized = NormalizeZoomScale(current);
        return MainContentZoomSteps.LastOrDefault(step => step < normalized - 0.001, MinMainContentZoomScale);
    }

    private static double GetNextZoomStep(double current)
    {
        var normalized = NormalizeZoomScale(current);
        return MainContentZoomSteps.FirstOrDefault(step => step > normalized + 0.001, MaxMainContentZoomScale);
    }

    private static double NormalizeZoomScale(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 1.0;
        }

        return Math.Clamp(value, MinMainContentZoomScale, MaxMainContentZoomScale);
    }
}
