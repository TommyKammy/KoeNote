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
using KoeNote.App.Services.Review;
using KoeNote.App.Services.SystemStatus;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly JobRepository _jobRepository;
    private readonly JobLogRepository _jobLogRepository;
    private readonly StageProgressRepository _stageProgressRepository;
    private readonly AsrSettingsRepository _asrSettingsRepository;
    private readonly AudioPreprocessWorker _audioPreprocessWorker;
    private readonly TranscriptSegmentRepository _transcriptSegmentRepository;
    private readonly AsrWorker _asrWorker;
    private readonly ReviewWorker _reviewWorker;
    private readonly JobRunCoordinator _jobRunCoordinator;
    private readonly CorrectionDraftRepository _correctionDraftRepository;
    private readonly ReviewOperationService _reviewOperationService;
    private readonly TranscriptEditService _transcriptEditService;
    private readonly CorrectionMemoryService _correctionMemoryService;
    private readonly TranscriptExportService _transcriptExportService;
    private readonly TextDiffService _textDiffService = new();
    private readonly StatusBarInfoService _statusBarInfoService;
    private readonly DispatcherTimer _statusRefreshTimer;
    private StatusBarInfo _statusBarInfo;
    private bool _isStatusRefreshInProgress;
    private JobSummary? _selectedJob;
    private TranscriptSegmentPreview? _selectedSegment;
    private CorrectionDraft? _selectedCorrectionDraft;
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
        _jobRunCoordinator = new JobRunCoordinator(
            Paths,
            _jobRepository,
            _stageProgressRepository,
            _jobLogRepository,
            _audioPreprocessWorker,
            _asrWorker,
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
        var asrSettings = _asrSettingsRepository.Load();
        _asrContextText = asrSettings.ContextText;
        _asrHotwordsText = asrSettings.HotwordsText;
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
        RunSelectedJobCommand = new RelayCommand(RunSelectedJobAsync, () => SelectedJob is not null && !IsRunInProgress);
        CancelCommand = new RelayCommand(CancelRunAsync, () => IsRunInProgress);
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

        RefreshSpeakerFilters();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppPaths Paths { get; }

    public string AppMode => "Offline";

    public string GpuSummary => EnvironmentStatus.FirstOrDefault(item => item.Name == "nvidia-smi")?.Detail ?? "Unknown";

    public string AsrModel => "VibeVoice ASR Q4";

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
                : string.Join(Environment.NewLine, missingItems.Select(static item => $"{item.Name}: {item.Detail}"));
        }
    }

    public ObservableCollection<StatusItem> EnvironmentStatus { get; } = [];

    public ObservableCollection<JobSummary> Jobs { get; } = [];

    public ObservableCollection<StageStatus> StageStatuses { get; } = [];

    public ObservableCollection<TranscriptSegmentPreview> Segments { get; } = [];

    public ObservableCollection<JobLogEntry> Logs { get; } = [];

    public ObservableCollection<CorrectionDraft> ReviewQueue { get; } = [];

    public ObservableCollection<DiffToken> DiffTokens { get; } = [];

    public ObservableCollection<string> SpeakerFilters { get; } = ["全話者"];

    public ICollectionView FilteredJobs { get; }

    public ICollectionView FilteredSegments { get; }

    public ICommand AddAudioCommand { get; }

    public ICommand DeleteJobCommand { get; }

    public ICommand ClearAllJobsCommand { get; }

    public ICommand RunSelectedJobCommand { get; }

    public ICommand CancelCommand { get; }

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
