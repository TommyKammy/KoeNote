using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Tests;

public sealed class JobRunCoordinatorTests
{
    [Fact]
    public async Task RunAsync_StopsWhenPreprocessDoesNotProduceAudio()
    {
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub(null),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);
        var updates = new List<JobRunUpdate>();

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty),
            updates.Add,
            CancellationToken.None);

        Assert.False(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.False(reviewStageRunner.SkipWasCalled);
        Assert.False(summaryStageRunner.RunWasCalled);
    }

    [Fact]
    public async Task RunAsync_StopsWhenAsrDoesNotProduceSegments()
    {
        var asrStageRunner = new AsrStageRunnerStub(null);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);
        var updates = new List<JobRunUpdate>();

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty),
            updates.Add,
            CancellationToken.None);

        Assert.True(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.False(reviewStageRunner.SkipWasCalled);
        Assert.False(summaryStageRunner.RunWasCalled);
    }

    [Fact]
    public async Task RunAsync_SkipsReviewWhenSettingIsDisabled()
    {
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);
        var updates = new List<JobRunUpdate>();

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty, EnableReviewStage: false),
            updates.Add,
            CancellationToken.None);

        Assert.True(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.True(reviewStageRunner.SkipWasCalled);
        Assert.True(summaryStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunAsync_RunsSummaryAfterAsrWhenSummaryEnabledAndReviewDisabled()
    {
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty, EnableReviewStage: false, EnableSummaryStage: true),
            _ => { },
            CancellationToken.None);

        Assert.True(reviewStageRunner.SkipWasCalled);
        Assert.True(summaryStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunAsync_SkipsSummaryWhenReviewFails()
    {
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub(runResult: false);
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty, EnableReviewStage: true, EnableSummaryStage: true),
            _ => { },
            CancellationToken.None);

        Assert.True(reviewStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.RunWasCalled);
        Assert.True(summaryStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunAsync_DoesNotTouchSummaryWhenReviewFailsAndSummaryIsDisabled()
    {
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub(runResult: false);
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty, EnableReviewStage: true, EnableSummaryStage: false),
            _ => { },
            CancellationToken.None);

        Assert.True(reviewStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunReviewOnlyAsync_RunsReviewWithoutPreprocessAsrOrSummary()
    {
        var segments = new[] { new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text") };
        var asrStageRunner = new AsrStageRunnerStub(null);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);

        var result = await coordinator.RunReviewOnlyAsync(
            CreateJob(),
            segments,
            _ => { },
            CancellationToken.None);

        Assert.True(result);
        Assert.False(asrStageRunner.WasCalled);
        Assert.True(reviewStageRunner.RunWasCalled);
        Assert.False(reviewStageRunner.SkipWasCalled);
        Assert.False(summaryStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.SkipWasCalled);
        Assert.Equal(segments, reviewStageRunner.ReceivedSegments);
    }

    [Fact]
    public async Task RunSummaryOnlyAsync_RunsSummaryWithoutPreprocessAsrOrReview()
    {
        var asrStageRunner = new AsrStageRunnerStub(null);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);

        await coordinator.RunSummaryOnlyAsync(
            CreateJob(),
            _ => { },
            CancellationToken.None);

        Assert.False(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.False(reviewStageRunner.SkipWasCalled);
        Assert.True(summaryStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunAsync_SkipsSummaryWhenManualReviewIsPending()
    {
        var job = CreateJob();
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub(onRun: () => job.UnreviewedDrafts = 2);
        var summaryStageRunner = new SummaryStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner,
            summaryStageRunner);

        await coordinator.RunAsync(
            job,
            new AsrSettings(string.Empty, string.Empty, EnableReviewStage: true, EnableSummaryStage: true),
            _ => { },
            CancellationToken.None);

        Assert.True(reviewStageRunner.RunWasCalled);
        Assert.False(summaryStageRunner.RunWasCalled);
        Assert.True(summaryStageRunner.SkipWasCalled);
        Assert.Equal("manual_review_pending", summaryStageRunner.SkipReason);
        Assert.Equal(2, job.UnreviewedDrafts);
    }

    private static JobSummary CreateJob()
    {
        return new JobSummary(
            "job-001",
            "Test",
            "test.wav",
            @"C:\audio\test.wav",
            "created",
            0,
            0,
            DateTimeOffset.Now);
    }

    private sealed class PreprocessStageRunnerStub(string? normalizedAudioPath) : IPreprocessStageRunner
    {
        public Task<string?> RunAsync(
            JobSummary job,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(normalizedAudioPath);
        }
    }

    private sealed class AsrStageRunnerStub(IReadOnlyList<TranscriptSegment>? segments) : IAsrStageRunner
    {
        public bool WasCalled { get; private set; }

        public Task<IReadOnlyList<TranscriptSegment>?> RunAsync(
            JobSummary job,
            string normalizedAudioPath,
            AsrSettings asrSettings,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.FromResult(segments);
        }
    }

    private sealed class ReviewStageRunnerStub(bool runResult = true, Action? onRun = null) : IReviewStageRunner
    {
        public bool RunWasCalled { get; private set; }

        public bool SkipWasCalled { get; private set; }

        public IReadOnlyList<TranscriptSegment>? ReceivedSegments { get; private set; }

        public Task<bool> RunAsync(
            JobSummary job,
            IReadOnlyList<TranscriptSegment> segments,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            RunWasCalled = true;
            ReceivedSegments = segments;
            onRun?.Invoke();
            return Task.FromResult(runResult);
        }

        public void Skip(JobSummary job, Action<JobRunUpdate> report)
        {
            SkipWasCalled = true;
        }
    }

    private sealed class SummaryStageRunnerStub : ISummaryStageRunner
    {
        public bool RunWasCalled { get; private set; }

        public bool SkipWasCalled { get; private set; }

        public string? SkipReason { get; private set; }

        public Task RunAsync(
            JobSummary job,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            RunWasCalled = true;
            return Task.CompletedTask;
        }

        public void Skip(JobSummary job, Action<JobRunUpdate> report, string reason)
        {
            SkipWasCalled = true;
            SkipReason = reason;
        }
    }
}
