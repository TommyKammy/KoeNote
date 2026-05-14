using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

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
        public Task<TranscriptSummaryChunkResult> SummarizeChunkAsync(
            TranscriptSummaryOptions options,
            TranscriptSummaryChunk chunk,
            CancellationToken cancellationToken = default)
        {
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
            return Task.FromResult($"## Overview{Environment.NewLine}{Environment.NewLine}Merged summary.");
        }
    }
}
