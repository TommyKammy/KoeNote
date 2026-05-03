using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
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
    private readonly AudioPreprocessWorker _audioPreprocessWorker;
    private readonly AsrWorker _asrWorker;
    private readonly ReviewWorker _reviewWorker;
    private JobSummary? _selectedJob;
    private string _latestLog;
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
        RunSelectedJobCommand = new RelayCommand(RunSelectedJobAsync, () => SelectedJob is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AppPaths Paths { get; }

    public string AppMode => "Offline";

    public string GpuSummary => EnvironmentStatus.FirstOrDefault(item => item.Name == "nvidia-smi")?.Detail ?? "Unknown";

    public string AsrModel => "VibeVoice ASR Q4";

    public string ReviewModel => "llm-jp Q4_K_M";

    public ObservableCollection<StatusItem> EnvironmentStatus { get; } = [];

    public ObservableCollection<JobSummary> Jobs { get; } = [];

    public ObservableCollection<StageStatus> StageStatuses { get; } = [];

    public ObservableCollection<TranscriptSegmentPreview> Segments { get; } = [];

    public ICommand AddAudioCommand { get; }

    public ICommand RunSelectedJobCommand { get; }

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
        SelectedJob = job;
        LatestLog = $"Registered audio job: {job.FileName}";
        _jobLogRepository.AddEvent(job.JobId, "created", "info", $"Registered audio file: {job.SourceAudioPath}");
        return job;
    }

    public async Task RunSelectedJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var job = SelectedJob;
        var stage = StageStatuses.First(item => item.Name == "音声変換");
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkPreprocessRunning(job);
        LatestLog = $"Running ffmpeg for {job.FileName}";

        try
        {
            var result = await _audioPreprocessWorker.NormalizeAsync(job, "ffmpeg", Paths);
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _jobRepository.MarkPreprocessSucceeded(job, result.NormalizedAudioPath);
            LatestLog = $"Generated normalized WAV: {result.NormalizedAudioPath}";

            await RunAsrAsync(job, result.NormalizedAudioPath);
        }
        catch (Exception exception)
        {
            stage.Status = "失敗";
            stage.ProgressPercent = 100;
            _jobRepository.MarkPreprocessFailed(job, "ffmpeg_failed");
            _jobLogRepository.AddEvent(job.JobId, "preprocess", "error", exception.Message);
            LatestLog = exception.Message;
        }
    }

    private async Task RunAsrAsync(JobSummary job, string normalizedAudioPath)
    {
        var stage = StageStatuses.First(item => item.Name == "ASR");
        var startedAt = DateTimeOffset.Now;
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkAsrRunning(job);
        _stageProgressRepository.Upsert(job.JobId, "asr", "running", 10, startedAt: startedAt);
        LatestLog = $"Running ASR for {job.FileName}";

        try
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "asr");
            var result = await _asrWorker.RunAsync(new AsrRunOptions(
                job.JobId,
                normalizedAudioPath,
                Paths.CrispAsrPath,
                Paths.VibeVoiceAsrModelPath,
                outputDirectory,
                Timeout: TimeSpan.FromHours(2)));

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
            _jobLogRepository.AddEvent(job.JobId, "asr", "info", $"Generated {result.Segments.Count} ASR segments: {result.NormalizedSegmentsPath}");
            LatestLog = $"ASR completed: {result.Segments.Count} segments";

            await RunReviewAsync(job, result.Segments);
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
            _jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{exception.Category}: {exception.Message}");
            LatestLog = $"ASR failed ({exception.Category}): {exception.Message}";
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
            _jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{AsrFailureCategory.Unknown}: {exception.Message}");
            LatestLog = $"ASR failed ({AsrFailureCategory.Unknown}): {exception.Message}";
        }
    }

    private static string FormatTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private async Task RunReviewAsync(JobSummary job, IReadOnlyList<TranscriptSegment> segments)
    {
        var stage = StageStatuses.First(item => item.Name == "推敲");
        var startedAt = DateTimeOffset.Now;
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.MarkReviewRunning(job);
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
                Timeout: TimeSpan.FromHours(2)));

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
            _jobLogRepository.AddEvent(job.JobId, "review", "info", $"Generated {result.Drafts.Count} correction drafts: {result.NormalizedDraftsPath}");
            LatestLog = $"Review completed: {result.Drafts.Count} drafts";

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
            _jobLogRepository.AddEvent(job.JobId, "review", "error", $"{exception.Category}: {exception.Message}");
            LatestLog = $"Review failed ({exception.Category}): {exception.Message}";
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
            _jobLogRepository.AddEvent(job.JobId, "review", "error", $"{ReviewFailureCategory.Unknown}: {exception.Message}");
            LatestLog = $"Review failed ({ReviewFailureCategory.Unknown}): {exception.Message}";
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

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
