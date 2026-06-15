using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class GpuRuntimeUpdateDetectionTests
{
    [Fact]
    public async Task AsrStageRunner_FailsBeforeAutoFallbackWhenNvidiaGpuRuntimeIsMissing()
    {
        await AssertAsrStageStopsBeforeTranscribeAsync(
            "faster-whisper-large-v3-turbo",
            AsrExecutionProfiles.CudaFloat16);
    }

    [Theory]
    [InlineData("faster-whisper-large-v3-turbo")]
    [InlineData("kotoba-whisper-v2.2-faster")]
    public async Task AsrStageRunner_FailsBeforeAutoDeviceWorkerWhenNvidiaGpuRuntimeIsMissing(string engineId)
    {
        await AssertAsrStageStopsBeforeTranscribeAsync(engineId, AsrExecutionProfiles.Auto);
    }

    private static async Task AssertAsrStageStopsBeforeTranscribeAsync(string engineId, string executionProfileId)
    {
        var paths = TestDatabase.CreateReadyPaths();
        var audioPath = Path.Combine(paths.Root, "meeting.wav");
        File.WriteAllText(audioPath, string.Empty);
        var job = new JobRepository(paths).CreateFromAudio(audioPath);
        var engine = new CapturingAsrEngine(engineId);
        var runner = new AsrStageRunner(
            paths,
            new JobRepository(paths),
            new StageProgressRepository(paths),
            new JobLogRepository(paths),
            new AsrEngineRegistry([engine]),
            new InstalledModelRepository(paths),
            CreateDiarizationService(paths),
            new CorrectionMemoryService(paths),
            new FixedHostResourceProbe(nvidiaGpuDetected: true));

        var result = await runner.RunAsync(
            job,
            audioPath,
            new AsrSettings(
                string.Empty,
                string.Empty,
                engineId,
                ExecutionProfileId: executionProfileId),
            _ => { },
            CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, engine.TranscribeCallCount);
        var logs = new JobLogRepository(paths).ReadLatest(job.JobId);
        Assert.Contains(logs, log =>
            log.Stage == "asr" &&
            log.Message.Contains("ASR GPU runtime is not ready", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReviewStageRunner_FailsBeforeCpuFallbackWhenNvidiaGpuRuntimeIsMissing()
    {
        var paths = TestDatabase.CreateReadyPaths();
        Directory.CreateDirectory(Path.GetDirectoryName(paths.LlamaCompletionPath)!);
        File.WriteAllText(paths.LlamaCompletionPath, "runtime");
        var job = CreateReviewReadyJob(paths, "job-review-runtime-missing");
        var segments = SaveSegments(paths, job.JobId);
        var reviewWorker = new ReviewWorker(
            new ThrowingProcessRunner(),
            new ReviewCommandBuilder(),
            new ReviewPromptBuilder(),
            new ReviewJsonNormalizer(),
            new ReviewResultStore(),
            new CorrectionDraftRepository(paths));
        var runner = new ReviewStageRunner(
            paths,
            new JobRepository(paths),
            new StageProgressRepository(paths),
            new JobLogRepository(paths),
            new InstalledModelRepository(paths),
            new SetupStateService(paths),
            reviewWorker,
            new FixedHostResourceProbe(nvidiaGpuDetected: true));

        var result = await runner.RunAsync(job, segments, _ => { }, CancellationToken.None);

        Assert.False(result);
        var logs = new JobLogRepository(paths).ReadLatest(job.JobId);
        Assert.Contains(logs, log =>
            log.Stage == "review" &&
            log.Message.Contains("Review GPU runtime is not ready", StringComparison.OrdinalIgnoreCase));
    }

    private static ScriptedDiarizationService CreateDiarizationService(AppPaths paths)
    {
        return new ScriptedDiarizationService(
            paths,
            new ThrowingProcessRunner(),
            new DiarizationJsonNormalizer(),
            new DiarizationSegmentAssigner(),
            new TranscriptSegmentRepository(paths),
            new AsrResultStore());
    }

    private static JobSummary CreateReviewReadyJob(AppPaths paths, string jobId)
    {
        TestDatabase.InsertReviewReadyJob(paths, jobId, "meeting");
        var now = DateTimeOffset.Now;
        return new JobSummary(
            jobId,
            "meeting",
            "meeting.wav",
            "meeting.wav",
            "review_ready",
            90,
            0,
            now,
            now);
    }

    private static IReadOnlyList<TranscriptSegment> SaveSegments(AppPaths paths, string jobId)
    {
        var segments = new[]
        {
            new TranscriptSegment("000001", jobId, 0, 1, "Speaker_0", "raw", "raw")
        };
        new TranscriptSegmentRepository(paths).SaveSegments(segments);
        return segments;
    }

    private sealed class CapturingAsrEngine(string engineId) : IAsrEngine
    {
        public string EngineId => engineId;

        public string DisplayName => engineId;

        public int TranscribeCallCount { get; private set; }

        public Task<AsrEngineCheckResult> CheckAsync(
            AsrEngineConfig config,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AsrEngineCheckResult(true, []));
        }

        public Task<AsrResult> TranscribeAsync(
            AsrInput input,
            AsrEngineConfig config,
            AsrOptions options,
            CancellationToken cancellationToken = default)
        {
            TranscribeCallCount++;
            return Task.FromResult(new AsrResult(
                "asr-run",
                input.JobId,
                Path.Combine(config.OutputDirectory, "raw.json"),
                Path.Combine(config.OutputDirectory, "segments.json"),
                [],
                TimeSpan.FromMilliseconds(1)));
        }
    }

    private sealed class ThrowingProcessRunner : ExternalProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            throw new InvalidOperationException("Process should not be started.");
        }
    }

    private sealed class FixedHostResourceProbe(bool nvidiaGpuDetected) : ISetupHostResourceProbe
    {
        public SetupHostResources GetResources()
        {
            return new SetupHostResources(
                32L * 1024 * 1024 * 1024,
                nvidiaGpuDetected ? 12 : null,
                nvidiaGpuDetected,
                8,
                nvidiaGpuDetected ? "NVIDIA GPU detected" : "No NVIDIA GPU detected");
        }
    }
}
