using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

[Collection(Gemma12BEnvironmentCollection.Name)]
public sealed class LlmStageLoggingTests
{
    [Fact]
    public async Task ReviewStageRunner_WritesLlmExecutionSettingsToJobLog()
    {
        var paths = TestDatabase.CreateReadyPaths();
        PrepareRuntimeFiles(paths);
        var job = CreateJob(paths, "job-review");
        var segments = SaveSegments(paths, job.JobId);
        var reviewWorker = new ReviewWorker(
            new JsonArrayProcessRunner(),
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
            reviewWorker);

        await runner.RunAsync(job, segments, _ => { }, CancellationToken.None);

        var logs = new JobLogRepository(paths).ReadLatest(job.JobId);
        Assert.Contains(logs, entry =>
            entry.Stage == "review" &&
            entry.Message.Contains("LLM task=review", StringComparison.Ordinal) &&
            entry.Message.Contains("profile=builtin:gemma-4-e4b-it-q4-k-m:gemma:balanced", StringComparison.Ordinal) &&
            entry.Message.Contains("generation=gemma-review-balanced", StringComparison.Ordinal) &&
            entry.Message.Contains("json_schema=true", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SummaryStageRunner_WritesLlmExecutionSettingsToJobLog()
    {
        var paths = TestDatabase.CreateReadyPaths();
        PrepareRuntimeFiles(paths);
        var job = CreateJob(paths, "job-summary");
        SaveSegments(paths, job.JobId);
        var summaryService = new TranscriptSummaryService(
            new TranscriptReadRepository(paths),
            new TranscriptDerivativeRepository(paths),
            new FakeSummaryRuntime());
        var runner = new SummaryStageRunner(
            paths,
            new JobRepository(paths),
            new StageProgressRepository(paths),
            new JobLogRepository(paths),
            new InstalledModelRepository(paths),
            new SetupStateService(paths),
            summaryService);

        var updates = new List<JobRunUpdate>();
        await runner.RunAsync(job, updates.Add, CancellationToken.None);

        var logs = new JobLogRepository(paths).ReadLatest(job.JobId);
        Assert.Contains(logs, entry =>
            entry.Stage == "summary" &&
            entry.Message.Contains("LLM task=summary", StringComparison.Ordinal) &&
            entry.Message.Contains("profile=builtin:gemma-4-e4b-it-q4-k-m:gemma:balanced", StringComparison.Ordinal) &&
            entry.Message.Contains("prompt=gemma-structured", StringComparison.Ordinal) &&
            entry.Message.Contains("generation=gemma-summary-balanced", StringComparison.Ordinal) &&
            entry.Message.Contains("max_tokens=1024", StringComparison.Ordinal) &&
            entry.Message.Contains("validation=markdown_summary_sections", StringComparison.Ordinal));
        Assert.Contains(updates, update =>
            update.Stage == JobRunStage.Summary &&
            update.StageState == JobRunStageState.Running &&
            update.StageProgressPercent == JobRunProgressPlan.SummaryRunning);

        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, progress_percent FROM stage_progress WHERE stage_id = $stage_id;";
        command.Parameters.AddWithValue("$stage_id", $"{job.JobId}-summary");

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("succeeded", reader.GetString(0));
        Assert.Equal(JobRunProgressPlan.Completed, reader.GetInt32(1));
    }

    [Fact]
    public async Task SummaryStageRunner_UsesGemma12BMtpForHighAccuracyPreset()
    {
        var previousMtp = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable);
        var paths = TestDatabase.CreateReadyPaths();
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, null);
            PrepareRuntimeFiles(paths);
            var modelPath = Path.Combine(paths.DefaultModelStorageRoot, "review", Gemma12BLocalValidation.ModelId, "gemma-4-12b-it-qat-q4_0.gguf");
            var draftPath = Gemma12BLocalValidation.ResolveMtpDraftModelPath(paths.UserModels);
            Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(draftPath)!);
            File.WriteAllText(modelPath, "model");
            File.WriteAllText(draftPath, "draft");
            File.WriteAllText(Gemma12BLocalValidation.ResolveLlamaServerPath(paths.LlamaCompletionPath), "server");
            new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
            {
                SelectedModelPresetId = "high_accuracy",
                SelectedReviewModelId = Gemma12BLocalValidation.ModelId
            });
            var job = CreateJob(paths, "job-summary-mtp");
            SaveSegments(paths, job.JobId);
            var fakeRuntime = new FakeSummaryRuntime();
            var runner = CreateSummaryRunner(paths, fakeRuntime);

            await runner.RunAsync(job, _ => { }, CancellationToken.None);

            Assert.NotNull(fakeRuntime.LastOptions);
            Assert.Equal(Gemma12BLocalValidation.ModelId, fakeRuntime.LastOptions.ModelId);
            Assert.True(fakeRuntime.LastOptions.UseLlamaServerChatMtp);
            Assert.EndsWith("llama-server.exe", fakeRuntime.LastOptions.LlamaServerPath, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(draftPath, fakeRuntime.LastOptions.MtpDraftModelPath);
            Assert.Contains("runtime=llama-server-chat-mtp", fakeRuntime.LastOptions.GenerationProfile, StringComparison.Ordinal);
            var logs = new JobLogRepository(paths).ReadLatest(job.JobId);
            Assert.Contains(logs, entry =>
                entry.Stage == "summary" &&
                entry.Message.Contains("Gemma 4 12B MTP server runtime enabled for summary", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, previousMtp);
        }
    }

    [Fact]
    public async Task SummaryStageRunner_FallsBackToDirectModelWhenGemma12BMtpIsDisabled()
    {
        var previousMtp = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable);
        var paths = TestDatabase.CreateReadyPaths();
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, "0");
            PrepareRuntimeFiles(paths);
            new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
            {
                SelectedModelPresetId = "high_accuracy",
                SelectedReviewModelId = Gemma12BLocalValidation.ModelId
            });
            var job = CreateJob(paths, "job-summary-mtp-disabled");
            SaveSegments(paths, job.JobId);
            var fakeRuntime = new FakeSummaryRuntime();
            var runner = CreateSummaryRunner(paths, fakeRuntime);

            await runner.RunAsync(job, _ => { }, CancellationToken.None);

            Assert.NotNull(fakeRuntime.LastOptions);
            Assert.Equal(ReviewModelSelectionResolver.DefaultReviewModelId, fakeRuntime.LastOptions.ModelId);
            Assert.False(fakeRuntime.LastOptions.UseLlamaServerChatMtp);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, previousMtp);
        }
    }

    private static SummaryStageRunner CreateSummaryRunner(AppPaths paths, FakeSummaryRuntime fakeRuntime)
    {
        return new SummaryStageRunner(
            paths,
            new JobRepository(paths),
            new StageProgressRepository(paths),
            new JobLogRepository(paths),
            new InstalledModelRepository(paths),
            new SetupStateService(paths),
            new TranscriptSummaryService(
                new TranscriptReadRepository(paths),
                new TranscriptDerivativeRepository(paths),
                fakeRuntime));
    }

    private static JobSummary CreateJob(AppPaths paths, string jobId)
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
            new TranscriptSegment("000001", jobId, 0, 1, "Speaker_0", "raw one", "raw one"),
            new TranscriptSegment("000002", jobId, 1, 2, "Speaker_1", "raw two", "raw two")
        };
        new TranscriptSegmentRepository(paths).SaveSegments(segments);
        return segments;
    }

    private static void PrepareRuntimeFiles(AppPaths paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.LlamaCompletionPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.ReviewModelPath)!);
        File.WriteAllText(paths.LlamaCompletionPath, "runtime");
        File.WriteAllText(paths.ReviewModelPath, "model");
    }

    private sealed class JsonArrayProcessRunner : ExternalProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            return Task.FromResult(new ProcessRunResult(0, TimeSpan.FromMilliseconds(10), "[]", ""));
        }
    }

    private sealed class FakeSummaryRuntime : ITranscriptSummaryRuntime
    {
        public TranscriptSummaryOptions? LastOptions { get; private set; }

        public Task<TranscriptSummaryChunkResult> SummarizeChunkAsync(
            TranscriptSummaryOptions options,
            TranscriptSummaryChunk chunk,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult(new TranscriptSummaryChunkResult(
                chunk,
                $"## Overview{Environment.NewLine}{Environment.NewLine}Summary for {chunk.SourceSegmentIds}.",
                TimeSpan.FromMilliseconds(10)));
        }

        public Task<string> MergeSummariesAsync(
            TranscriptSummaryOptions options,
            IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
            CancellationToken cancellationToken = default)
        {
            LastOptions = options;
            return Task.FromResult($"## Overview{Environment.NewLine}{Environment.NewLine}Merged summary.");
        }
    }
}
