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
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub(null),
            asrStageRunner,
            reviewStageRunner);
        var updates = new List<JobRunUpdate>();

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty),
            updates.Add,
            CancellationToken.None);

        Assert.False(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.False(reviewStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunAsync_StopsWhenAsrDoesNotProduceSegments()
    {
        var asrStageRunner = new AsrStageRunnerStub(null);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner);
        var updates = new List<JobRunUpdate>();

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty),
            updates.Add,
            CancellationToken.None);

        Assert.True(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.False(reviewStageRunner.SkipWasCalled);
    }

    [Fact]
    public async Task RunAsync_SkipsReviewWhenSettingIsDisabled()
    {
        var asrStageRunner = new AsrStageRunnerStub([new TranscriptSegment("segment-001", "job-001", 0, 1, "Speaker_0", "text")]);
        var reviewStageRunner = new ReviewStageRunnerStub();
        var coordinator = new JobRunCoordinator(
            new PreprocessStageRunnerStub("normalized.wav"),
            asrStageRunner,
            reviewStageRunner);
        var updates = new List<JobRunUpdate>();

        await coordinator.RunAsync(
            CreateJob(),
            new AsrSettings(string.Empty, string.Empty, EnableReviewStage: false),
            updates.Add,
            CancellationToken.None);

        Assert.True(asrStageRunner.WasCalled);
        Assert.False(reviewStageRunner.RunWasCalled);
        Assert.True(reviewStageRunner.SkipWasCalled);
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

    private sealed class ReviewStageRunnerStub() : IReviewStageRunner
    {
        public bool RunWasCalled { get; private set; }

        public bool SkipWasCalled { get; private set; }

        public Task RunAsync(
            JobSummary job,
            IReadOnlyList<TranscriptSegment> segments,
            Action<JobRunUpdate> report,
            CancellationToken cancellationToken)
        {
            RunWasCalled = true;
            return Task.CompletedTask;
        }

        public void Skip(JobSummary job, Action<JobRunUpdate> report)
        {
            SkipWasCalled = true;
        }
    }
}
