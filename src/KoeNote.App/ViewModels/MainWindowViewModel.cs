using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly JobRepository _jobRepository;
    private readonly JobLogRepository _jobLogRepository;
    private readonly StageProgressRepository _stageProgressRepository;
    private readonly AsrSettingsRepository _asrSettingsRepository;
    private readonly AudioPreprocessWorker _audioPreprocessWorker;
    private readonly AsrWorker _asrWorker;
    private readonly ReviewWorker _reviewWorker;
    private JobSummary? _selectedJob;
    private CancellationTokenSource? _runCancellation;
    private CancellationTokenSource? _asrSettingsSaveDebounce;
    private string _latestLog;
    private string _jobSearchText = string.Empty;
    private string _segmentSearchText = string.Empty;
    private string _selectedSpeakerFilter = "全話者";
    private string _asrContextText = string.Empty;
    private string _asrHotwordsText = string.Empty;
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
        var processRunner = new ExternalProcessRunner();
        _audioPreprocessWorker = new AudioPreprocessWorker(processRunner, _stageProgressRepository, _jobLogRepository);
        _asrWorker = new AsrWorker(
            processRunner,
            new AsrCommandBuilder(),
            new AsrJsonNormalizer(),
            new AsrResultStore(),
            new TranscriptSegmentRepository(Paths));
        _reviewWorker = new ReviewWorker(
            processRunner,
            new ReviewCommandBuilder(),
            new ReviewPromptBuilder(),
            new ReviewJsonNormalizer(),
            new ReviewResultStore(),
            new CorrectionDraftRepository(Paths));

        var toolStatus = new ToolStatusService(Paths);
        foreach (var item in toolStatus.GetStatusItems())
        {
            EnvironmentStatus.Add(item);
        }

        foreach (var stageName in new[] { "音声変換", "ASR", "推敲", "レビュー", "出力" })
        {
            StageStatuses.Add(new StageStatus(stageName));
        }

        _latestLog = $"Initialized AppData at {Paths.Root}";
        var asrSettings = _asrSettingsRepository.Load();
        _asrContextText = asrSettings.ContextText;
        _asrHotwordsText = asrSettings.HotwordsText;
        LoadJobs();
        RefreshLogs();

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

        AddAudioCommand = new RelayCommand(AddAudioAsync);
        RunSelectedJobCommand = new RelayCommand(RunSelectedJobAsync, () => SelectedJob is not null && !IsRunInProgress);
        CancelCommand = new RelayCommand(CancelRunAsync, () => IsRunInProgress);

        FilteredJobs = CollectionViewSource.GetDefaultView(Jobs);
        FilteredJobs.Filter = FilterJob;
        FilteredSegments = CollectionViewSource.GetDefaultView(Segments);
        FilteredSegments.Filter = FilterSegment;
        RefreshSpeakerFilters();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppPaths Paths { get; }

    public string AppMode => "Offline";

    public string GpuSummary => EnvironmentStatus.FirstOrDefault(item => item.Name == "nvidia-smi")?.Detail ?? "Unknown";

    public string AsrModel => "VibeVoice ASR Q4";

    public string ReviewModel => "llm-jp Q4_K_M";

    public string StoragePath => Paths.Root;

    public string DiskFreeSummary => GetDiskFreeSummary();

    public string MemorySummary => GetMemorySummary();

    public string CpuSummary => GetCpuSummary();

    public string GpuUsageSummary => GetGpuUsageSummary();

    public ObservableCollection<StatusItem> EnvironmentStatus { get; } = [];

    public ObservableCollection<JobSummary> Jobs { get; } = [];

    public ObservableCollection<StageStatus> StageStatuses { get; } = [];

    public ObservableCollection<TranscriptSegmentPreview> Segments { get; } = [];

    public ObservableCollection<JobLogEntry> Logs { get; } = [];

    public ObservableCollection<string> SpeakerFilters { get; } = ["全話者"];

    public ICollectionView FilteredJobs { get; }

    public ICollectionView FilteredSegments { get; }

    public ICommand AddAudioCommand { get; }

    public ICommand RunSelectedJobCommand { get; }

    public ICommand CancelCommand { get; }

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
            }
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
        private set => SetField(ref _originalText, value);
    }

    public string SuggestedText
    {
        get => _suggestedText;
        private set => SetField(ref _suggestedText, value);
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

    private Task AddAudioAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "音声ファイルを選択",
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.flac;*.aac;*.ogg;*.opus|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        RegisterAudioFile(dialog.FileName);
        return Task.CompletedTask;
    }

    public JobSummary RegisterAudioFile(string audioPath)
    {
        var job = _jobRepository.CreateFromAudio(audioPath);
        Jobs.Insert(0, job);
        FilteredJobs.Refresh();
        SelectedJob = job;
        LatestLog = $"Registered audio job: {job.FileName}";
        _jobLogRepository.AddEvent(job.JobId, "created", "info", $"Registered audio file: {job.SourceAudioPath}");
        RefreshLogs();
        OnPropertyChanged(nameof(JobCountSummary));
        return job;
    }

    public void RegisterAudioFiles(IEnumerable<string> audioPaths)
    {
        var registered = 0;
        foreach (var audioPath in audioPaths.Where(IsSupportedAudioFile))
        {
            RegisterAudioFile(audioPath);
            registered++;
        }

        if (registered == 0)
        {
            LatestLog = "音声ファイルをドロップしてください。";
            return;
        }

        LatestLog = $"{registered}件の音声ファイルを登録しました。";
    }

    public async Task RunSelectedJobAsync()
    {
        if (SelectedJob is null || IsRunInProgress)
        {
            return;
        }

        var job = SelectedJob;
        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsRunInProgress = true;
        var stage = StageStatuses.First(item => item.Name == "音声変換");
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkPreprocessRunning(job);
        RefreshJobViews();
        LatestLog = $"Running ffmpeg for {job.FileName}";

        try
        {
            var result = await _audioPreprocessWorker.NormalizeAsync(job, "ffmpeg", Paths, cancellation.Token);
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _jobRepository.MarkPreprocessSucceeded(job, result.NormalizedAudioPath);
            RefreshJobViews();
            LatestLog = $"Generated normalized WAV: {result.NormalizedAudioPath}";

            await RunAsrAsync(job, result.NormalizedAudioPath, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            stage.Status = "中止";
            stage.ProgressPercent = 100;
            _jobRepository.MarkCancelled(job, "preprocess");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "preprocess", "info", "Run was cancelled.");
            LatestLog = "実行をキャンセルしました。";
            RefreshLogs();
        }
        catch (Exception exception)
        {
            stage.Status = "失敗";
            stage.ProgressPercent = 100;
            _jobRepository.MarkPreprocessFailed(job, "ffmpeg_failed");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "preprocess", "error", exception.Message);
            LatestLog = exception.Message;
            RefreshLogs();
        }
        finally
        {
            _runCancellation = null;
            IsRunInProgress = false;
        }
    }

    private async Task RunAsrAsync(JobSummary job, string normalizedAudioPath, CancellationToken cancellationToken)
    {
        var stage = StageStatuses.First(item => item.Name == "ASR");
        var startedAt = DateTimeOffset.Now;
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkAsrRunning(job);
        RefreshJobViews();
        _stageProgressRepository.Upsert(job.JobId, "asr", "running", 10, startedAt: startedAt);
        LatestLog = $"Running ASR for {job.FileName}";

        try
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "asr");
            SaveAsrSettings();
            var asrSettings = new AsrSettings(AsrContextText, AsrHotwordsText);
            var result = await _asrWorker.RunAsync(new AsrRunOptions(
                job.JobId,
                normalizedAudioPath,
                Paths.CrispAsrPath,
                Paths.VibeVoiceAsrModelPath,
                outputDirectory,
                asrSettings.Hotwords,
                string.IsNullOrWhiteSpace(asrSettings.ContextText) ? null : asrSettings.ContextText,
                Timeout: TimeSpan.FromHours(2)),
                cancellationToken);

            Segments.Clear();
            foreach (var segment in result.Segments)
            {
                Segments.Add(new TranscriptSegmentPreview(
                    FormatTimestamp(segment.StartSeconds),
                    FormatTimestamp(segment.EndSeconds),
                    segment.SpeakerId ?? "",
                    segment.NormalizedText ?? segment.RawText,
                    "候補なし",
                    segment.SegmentId));
            }
            RefreshSpeakerFilters();
            FilteredSegments.Refresh();

            var finishedAt = DateTimeOffset.Now;
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "succeeded",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            _jobRepository.MarkAsrSucceeded(job);
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "info", $"Generated {result.Segments.Count} ASR segments: {result.NormalizedSegmentsPath}");
            LatestLog = $"ASR completed: {result.Segments.Count} segments";
            RefreshLogs();

            await RunReviewAsync(job, result.Segments, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "中止";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            _jobRepository.MarkCancelled(job, "asr");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "info", "Run was cancelled.");
            LatestLog = "ASRをキャンセルしました。";
            RefreshLogs();
        }
        catch (AsrWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = $"失敗: {exception.Category}";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            _jobRepository.MarkAsrFailed(job, exception.Category.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{exception.Category}: {exception.Message}");
            LatestLog = $"ASR failed ({exception.Category}): {exception.Message}";
            RefreshLogs();
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "失敗: Unknown";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: AsrFailureCategory.Unknown.ToString());
            _jobRepository.MarkAsrFailed(job, AsrFailureCategory.Unknown.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{AsrFailureCategory.Unknown}: {exception.Message}");
            LatestLog = $"ASR failed ({AsrFailureCategory.Unknown}): {exception.Message}";
            RefreshLogs();
        }
    }

    private static string FormatTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private async Task RunReviewAsync(JobSummary job, IReadOnlyList<TranscriptSegment> segments, CancellationToken cancellationToken)
    {
        var stage = StageStatuses.First(item => item.Name == "推敲");
        var startedAt = DateTimeOffset.Now;
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkReviewRunning(job);
        RefreshJobViews();
        _stageProgressRepository.Upsert(job.JobId, "review", "running", 10, startedAt: startedAt);
        LatestLog = $"Running review for {job.FileName}";

        try
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "review");
            var result = await _reviewWorker.RunAsync(new ReviewRunOptions(
                job.JobId,
                Paths.LlamaCompletionPath,
                Paths.ReviewModelPath,
                outputDirectory,
                segments,
                MinConfidence: 0.5,
                Timeout: TimeSpan.FromHours(2)),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "succeeded",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            _jobRepository.MarkReviewSucceeded(job, result.Drafts.Count);
            job.UnreviewedDrafts = result.Drafts.Count;
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "info", $"Generated {result.Drafts.Count} correction drafts: {result.NormalizedDraftsPath}");
            LatestLog = $"Review completed: {result.Drafts.Count} drafts";
            RefreshLogs();

            var firstDraft = result.Drafts.FirstOrDefault();
            if (firstDraft is not null)
            {
                ReviewIssueType = firstDraft.IssueType;
                OriginalText = firstDraft.OriginalText;
                SuggestedText = firstDraft.SuggestedText;
                ReviewReason = firstDraft.Reason;
                Confidence = firstDraft.Confidence;
                UpdateSegmentReviewStates(result.Drafts);
            }
            else
            {
                ClearReviewPreview();
            }
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "中止";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            _jobRepository.MarkCancelled(job, "review");
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "info", "Run was cancelled.");
            LatestLog = "推敲をキャンセルしました。";
            RefreshLogs();
        }
        catch (ReviewWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = $"失敗: {exception.Category}";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            _jobRepository.MarkReviewFailed(job, exception.Category.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "error", $"{exception.Category}: {exception.Message}");
            LatestLog = $"Review failed ({exception.Category}): {exception.Message}";
            RefreshLogs();
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            stage.Status = "失敗: Unknown";
            stage.ProgressPercent = 100;
            _stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: ReviewFailureCategory.Unknown.ToString());
            _jobRepository.MarkReviewFailed(job, ReviewFailureCategory.Unknown.ToString());
            RefreshJobViews();
            _jobLogRepository.AddEvent(job.JobId, "review", "error", $"{ReviewFailureCategory.Unknown}: {exception.Message}");
            LatestLog = $"Review failed ({ReviewFailureCategory.Unknown}): {exception.Message}";
            RefreshLogs();
        }
    }

    private Task CancelRunAsync()
    {
        _runCancellation?.Cancel();
        LatestLog = "キャンセルを要求しました。";
        return Task.CompletedTask;
    }

    private void SaveAsrSettings()
    {
        _asrSettingsSaveDebounce?.Cancel();
        _asrSettingsSaveDebounce = null;
        _asrSettingsRepository.Save(new AsrSettings(AsrContextText, AsrHotwordsText));
    }

    private void ScheduleSaveAsrSettings()
    {
        _asrSettingsSaveDebounce?.Cancel();
        var cancellation = new CancellationTokenSource();
        _asrSettingsSaveDebounce = cancellation;
        _ = SaveAsrSettingsAfterDelayAsync(cancellation.Token);
    }

    private async Task SaveAsrSettingsAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
            _asrSettingsRepository.Save(new AsrSettings(AsrContextText, AsrHotwordsText));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void UpdateSegmentReviewStates(IReadOnlyList<CorrectionDraft> drafts)
    {
        var draftSegmentIds = drafts.Select(draft => draft.SegmentId).ToHashSet(StringComparer.Ordinal);
        for (var i = 0; i < Segments.Count; i++)
        {
            var preview = Segments[i];
            if (draftSegmentIds.Contains(preview.SegmentId))
            {
                Segments[i] = preview with { ReviewState = "推敲候補あり" };
            }
        }
    }

    private void ClearReviewPreview()
    {
        ReviewIssueType = "候補なし";
        OriginalText = string.Empty;
        SuggestedText = string.Empty;
        ReviewReason = "推敲候補は生成されませんでした。";
        Confidence = 0;
    }

    private void LoadJobs()
    {
        foreach (var job in _jobRepository.LoadRecent())
        {
            Jobs.Add(job);
        }

        SelectedJob = Jobs.FirstOrDefault();
        OnPropertyChanged(nameof(JobCountSummary));
    }

    private void RefreshLogs()
    {
        Logs.Clear();
        foreach (var entry in _jobLogRepository.ReadLatest(SelectedJob?.JobId))
        {
            Logs.Add(entry);
        }
    }

    private void RefreshJobViews()
    {
        FilteredJobs.Refresh();
        OnPropertyChanged(nameof(SelectedJobNormalizedAudioPath));
        OnPropertyChanged(nameof(SelectedJobUpdatedAt));
        OnPropertyChanged(nameof(SelectedJobUnreviewedDrafts));
    }

    private bool FilterJob(object item)
    {
        if (item is not JobSummary job || string.IsNullOrWhiteSpace(JobSearchText))
        {
            return true;
        }

        return job.Title.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase)
            || job.FileName.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase)
            || job.Status.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterSegment(object item)
    {
        if (item is not TranscriptSegmentPreview segment)
        {
            return false;
        }

        var speakerMatches = SelectedSpeakerFilter == "全話者"
            || string.Equals(segment.Speaker, SelectedSpeakerFilter, StringComparison.Ordinal);
        var textMatches = string.IsNullOrWhiteSpace(SegmentSearchText)
            || segment.Text.Contains(SegmentSearchText, StringComparison.OrdinalIgnoreCase)
            || segment.Speaker.Contains(SegmentSearchText, StringComparison.OrdinalIgnoreCase)
            || segment.ReviewState.Contains(SegmentSearchText, StringComparison.OrdinalIgnoreCase);

        return speakerMatches && textMatches;
    }

    private void RefreshSpeakerFilters()
    {
        var selected = SelectedSpeakerFilter;
        SpeakerFilters.Clear();
        SpeakerFilters.Add("全話者");
        foreach (var speaker in Segments.Select(segment => segment.Speaker).Where(static speaker => !string.IsNullOrWhiteSpace(speaker)).Distinct().Order())
        {
            SpeakerFilters.Add(speaker);
        }

        SelectedSpeakerFilter = SpeakerFilters.Contains(selected) ? selected : "全話者";
    }

    private static bool IsSupportedAudioFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".m4a" or ".flac" or ".aac" or ".ogg" or ".opus";
    }

    private string GetDiskFreeSummary()
    {
        try
        {
            var root = Path.GetPathRoot(Paths.Root);
            if (string.IsNullOrWhiteSpace(root))
            {
                return "空き容量 Unknown";
            }

            var drive = new DriveInfo(root);
            return $"空き容量 {drive.AvailableFreeSpace / 1024d / 1024d / 1024d:N1} GB";
        }
        catch (IOException)
        {
            return "空き容量 Unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "空き容量 Unknown";
        }
    }

    private static string GetMemorySummary()
    {
        using var process = Process.GetCurrentProcess();
        return $"MEM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

    private static string GetCpuSummary()
    {
        using var process = Process.GetCurrentProcess();
        var uptime = DateTimeOffset.Now - process.StartTime;
        if (uptime <= TimeSpan.Zero)
        {
            return "CPU Unknown";
        }

        var averageUsage = process.TotalProcessorTime.TotalMilliseconds / uptime.TotalMilliseconds / Environment.ProcessorCount * 100;
        return $"CPU {Math.Clamp(averageUsage, 0, 100):N0}%";
    }

    private static string GetGpuUsageSummary()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=utilization.gpu,memory.used --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return "GPU Unknown";
            }

            if (!process.WaitForExit(1200))
            {
                process.Kill(entireProcessTree: true);
                return "GPU Unknown";
            }

            var firstLine = process.StandardOutput.ReadToEnd()
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return "GPU Unknown";
            }

            var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return $"GPU {firstLine}";
            }

            return $"GPU {parts[0]}% / {parts[1]} MB";
        }
        catch
        {
            return "GPU Unknown";
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
