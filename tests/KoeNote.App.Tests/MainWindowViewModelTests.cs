using System.Collections;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void JobSearchText_FiltersJobsByTitleFileNameAndStatus()
    {
        var viewModel = CreateViewModel();
        viewModel.Jobs.Clear();
        viewModel.Jobs.Add(new JobSummary("job-001", "企画会議", "planning.wav", @"C:\audio\planning.wav", "登録済み", 0, 0, DateTimeOffset.Now));
        viewModel.Jobs.Add(new JobSummary("job-002", "レビュー会議", "review.wav", @"C:\audio\review.wav", "キャンセル済み", 10, 0, DateTimeOffset.Now));

        viewModel.JobSearchText = "キャンセル";

        var jobs = ViewItems<JobSummary>(viewModel.FilteredJobs);
        var job = Assert.Single(jobs);
        Assert.Equal("job-002", job.JobId);

        viewModel.JobSearchText = "planning";

        jobs = ViewItems<JobSummary>(viewModel.FilteredJobs);
        job = Assert.Single(jobs);
        Assert.Equal("job-001", job.JobId);
    }

    [Fact]
    public void SegmentSearchAndSpeakerFilter_CombinePredicates()
    {
        var viewModel = CreateViewModel();

        viewModel.SegmentSearchText = "サーバー";
        viewModel.SelectedSpeakerFilter = "Speaker_1";

        var segments = ViewItems<TranscriptSegmentPreview>(viewModel.FilteredSegments);
        var segment = Assert.Single(segments);
        Assert.Equal("Speaker_1", segment.Speaker);
        Assert.Contains("サーバー", segment.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsrSettings_RestoreAfterRecreatingViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var first = new MainWindowViewModel(paths)
        {
            AsrContextText = "製品開発会議",
            AsrHotwordsText = "KoeNote\r\nRTX 3060"
        };
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        var second = new MainWindowViewModel(paths);

        Assert.Equal(first.AsrContextText, second.AsrContextText);
        Assert.Equal(first.AsrHotwordsText, second.AsrHotwordsText);
    }

    [Fact]
    public async Task ReviewQueue_LoadsSelectedDraftAndAcceptCommandAdvances()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var job = new JobRepository(paths).CreateFromAudio(Path.Combine(root, "meeting.wav"));
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("segment-001", job.JobId, 0, 1, "Speaker_0", "ミギワ"),
            new TranscriptSegment("segment-002", job.JobId, 2, 3, "Speaker_1", "公正")
        ]);
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", job.JobId, "segment-001", "意味不明語", "ミギワ", "右側", "候補", 0.8),
            new CorrectionDraft("draft-002", job.JobId, "segment-002", "同音異義語", "公正", "構成", "候補", 0.7)
        ]);

        var viewModel = new MainWindowViewModel(paths);

        Assert.Equal("draft-001", viewModel.SelectedCorrectionDraftId);
        Assert.Equal("1 / 2", viewModel.DraftPositionText);

        viewModel.AcceptDraftCommand.Execute(null);
        Assert.False(viewModel.SelectNextDraftCommand.CanExecute(null));
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Equal("draft-002", viewModel.SelectedCorrectionDraftId);
        Assert.Equal("1 / 1", viewModel.DraftPositionText);
        Assert.Equal(1, viewModel.SelectedJobUnreviewedDrafts);
    }

    private static MainWindowViewModel CreateViewModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new MainWindowViewModel(new AppPaths(root, root, AppContext.BaseDirectory));
    }

    private static List<T> ViewItems<T>(IEnumerable view)
    {
        return view.Cast<T>().ToList();
    }
}
