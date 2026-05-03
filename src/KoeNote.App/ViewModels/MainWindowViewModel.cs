using System.Collections.ObjectModel;
using KoeNote.App.Models;
using KoeNote.App.Services;

namespace KoeNote.App.ViewModels;

public sealed class MainWindowViewModel
{
    public MainWindowViewModel()
    {
        Paths = new AppPaths();
        Paths.EnsureCreated();

        var database = new DatabaseInitializer(Paths);
        database.EnsureCreated();

        var toolStatus = new ToolStatusService(Paths);
        foreach (var item in toolStatus.GetStatusItems())
        {
            EnvironmentStatus.Add(item);
        }

        Jobs.Add(new JobSummary(
            "Phase 1 Smoke",
            "sample-meeting.m4a",
            "待機中",
            0,
            0,
            DateTimeOffset.Now));

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

    public AppPaths Paths { get; }

    public string AppMode => "Offline";

    public string GpuSummary => EnvironmentStatus.FirstOrDefault(item => item.Name == "nvidia-smi")?.Detail ?? "Unknown";

    public string AsrModel => "VibeVoice ASR Q4";

    public string ReviewModel => "llm-jp Q4_K_M";

    public ObservableCollection<StatusItem> EnvironmentStatus { get; } = [];

    public ObservableCollection<JobSummary> Jobs { get; } = [];

    public ObservableCollection<TranscriptSegmentPreview> Segments { get; } = [];

    public string ReviewIssueType => "意味不明語の疑い";

    public string OriginalText => "この仕様はサーバーのミギワで処理します。";

    public string SuggestedText => "この仕様はサーバーの右側で処理します。";

    public string ReviewReason => "文脈上「ミギワ」が不自然で、音の近い語として「右側」が候補になる。";

    public double Confidence => 0.62;

    public IReadOnlyList<string> StageLabels { get; } = ["音声変換", "ASR", "推敲", "レビュー", "出力"];

    public string LatestLog => $"Initialized AppData at {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)}\\KoeNote";
}
