using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Jobs;
using Microsoft.Win32;

namespace KoeNote.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly JobRepository _jobRepository;
    private readonly JobLogRepository _jobLogRepository;
    private readonly AudioPreprocessWorker _audioPreprocessWorker;
    private JobSummary? _selectedJob;
    private string _latestLog;

    public MainWindowViewModel()
    {
        Paths = new AppPaths();
        Paths.EnsureCreated();

        var database = new DatabaseInitializer(Paths);
        database.EnsureCreated();

        _jobRepository = new JobRepository(Paths);
        var stageProgressRepository = new StageProgressRepository(Paths);
        _jobLogRepository = new JobLogRepository(Paths);
        _audioPreprocessWorker = new AudioPreprocessWorker(new ExternalProcessRunner(), stageProgressRepository, _jobLogRepository);

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

    public string ReviewIssueType => "意味不明語の疑い";

    public string OriginalText => "この仕様はサーバーのミギワで処理します。";

    public string SuggestedText => "この仕様はサーバーの右側で処理します。";

    public string ReviewReason => "文脈上「ミギワ」が不自然で、音の近い語として「右側」が候補になる。";

    public double Confidence => 0.62;

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

        var job = _jobRepository.CreateFromAudio(dialog.FileName);
        Jobs.Insert(0, job);
        SelectedJob = job;
        LatestLog = $"Registered audio job: {job.FileName}";
        _jobLogRepository.AddEvent(job.JobId, "created", "info", $"Registered audio file: {job.SourceAudioPath}");
        return Task.CompletedTask;
    }

    private async Task RunSelectedJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var job = SelectedJob;
        var stage = StageStatuses.First(item => item.Name == "音声変換");
        stage.Status = "実行中";
        stage.ProgressPercent = 10;
        _jobRepository.UpdatePreprocessResult(job, "音声変換中", "preprocessing", 10, null);
        LatestLog = $"Running ffmpeg for {job.FileName}";

        try
        {
            var result = await _audioPreprocessWorker.NormalizeAsync(job, "ffmpeg", Paths);
            stage.Status = "成功";
            stage.ProgressPercent = 100;
            _jobRepository.UpdatePreprocessResult(job, "音声変換完了", "preprocessed", 100, result.NormalizedAudioPath);
            LatestLog = $"Generated normalized WAV: {result.NormalizedAudioPath}";
        }
        catch (Exception exception)
        {
            stage.Status = "失敗";
            stage.ProgressPercent = 100;
            _jobRepository.UpdatePreprocessResult(job, "音声変換失敗", "preprocessing_failed", 100, null, "ffmpeg_failed");
            _jobLogRepository.AddEvent(job.JobId, "preprocess", "error", exception.Message);
            LatestLog = exception.Message;
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
