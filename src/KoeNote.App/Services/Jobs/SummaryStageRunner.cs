using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Jobs;

public sealed class SummaryStageRunner(
    AppPaths paths,
    JobRepository jobRepository,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository,
    InstalledModelRepository installedModelRepository,
    SetupStateService setupStateService,
    TranscriptSummaryService summaryService) : ISummaryStageRunner
{
    public async Task RunAsync(
        JobSummary job,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Running, 10));
        jobRepository.MarkSummaryRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true));
        stageProgressRepository.Upsert(job.JobId, "summary", "running", 10, startedAt: startedAt);
        report(new JobRunUpdate(LatestLog: $"Running summary for {job.FileName}"));

        try
        {
            var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
            var modelId = ResolveReviewModelId();
            var sanitizerProfile = ResolveOutputSanitizerProfile(modelId);
            var tuning = ReviewRuntimeTuningProfiles.ForReviewModel(modelId);
            var result = await summaryService.SummarizeAsync(new TranscriptSummaryOptions(
                job.JobId,
                ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, modelId),
                ResolveReviewModelPath(modelId),
                Path.Combine(paths.Jobs, job.JobId, "summary"),
                modelId,
                "default",
                ChunkSegmentCount: tuning.ChunkSegmentCount,
                Timeout: tuning.Timeout,
                OutputSanitizerProfile: sanitizerProfile,
                ContextSize: tuning.ContextSize,
                GpuLayers: tuning.GpuLayers,
                MaxTokens: ResolveSummaryMaxTokens(tuning),
                Threads: tuning.Threads,
                ThreadsBatch: tuning.ThreadsBatch),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            var status = string.IsNullOrWhiteSpace(result.Content) ? "failed" : "succeeded";
            report(new JobRunUpdate(
                JobRunStage.Summary,
                status == "succeeded" ? JobRunStageState.Succeeded : JobRunStageState.Failed,
                100,
                result.Duration,
                status == "succeeded" ? null : "summary_failed"));
            stageProgressRepository.Upsert(
                job.JobId,
                "summary",
                status,
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                errorCategory: status == "succeeded" ? null : "summary_failed");

            if (status == "succeeded")
            {
                jobRepository.MarkSummarySucceeded(job);
                jobLogRepository.AddEvent(job.JobId, "summary", "info", $"Generated summary derivative: {result.DerivativeId}");
                report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: "Summary completed."));
                return;
            }

            jobRepository.MarkSummaryFailed(job, "summary_failed");
            jobLogRepository.AddEvent(job.JobId, "summary", "error", $"Summary failed derivative: {result.DerivativeId}");
            report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: "Summary failed."));
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Cancelled, 100, finishedAt - startedAt));
            stageProgressRepository.Upsert(
                job.JobId,
                "summary",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            jobRepository.MarkCancelled(job, "summary");
            jobLogRepository.AddEvent(job.JobId, "summary", "info", "Run was cancelled.");
            report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: "Summary cancelled."));
        }
        catch (ReviewWorkerException exception)
        {
            MarkFailed(job, report, startedAt, exception.Category.ToString(), exception.Message);
        }
        catch (Exception exception)
        {
            MarkFailed(job, report, startedAt, ReviewFailureCategory.Unknown.ToString(), exception.Message);
        }
    }

    public void Skip(JobSummary job, Action<JobRunUpdate> report, string reason)
    {
        var now = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Skipped, 100, TimeSpan.Zero));
        stageProgressRepository.Upsert(
            job.JobId,
            "summary",
            "skipped",
            100,
            now,
            now,
            0,
            errorCategory: reason);
        jobLogRepository.AddEvent(job.JobId, "summary", "info", $"Summary stage skipped: {reason}.");
        report(new JobRunUpdate(
            RefreshJobViews: true,
            RefreshLogs: true,
            LatestLog: $"Summary stage skipped: {reason}."));
    }

    private void MarkFailed(
        JobSummary job,
        Action<JobRunUpdate> report,
        DateTimeOffset startedAt,
        string errorCategory,
        string message)
    {
        var finishedAt = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Failed, 100, finishedAt - startedAt, errorCategory));
        stageProgressRepository.Upsert(
            job.JobId,
            "summary",
            "failed",
            100,
            startedAt,
            finishedAt,
            (finishedAt - startedAt).TotalSeconds,
            errorCategory: errorCategory);
        jobRepository.MarkSummaryFailed(job, errorCategory);
        jobLogRepository.AddEvent(job.JobId, "summary", "error", $"{errorCategory}: {message}");
        report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: $"Summary failed ({errorCategory}): {message}"));
    }

    private string ResolveReviewModelId()
    {
        var state = setupStateService.Load();
        var selectedReviewModelId = state.SelectedReviewModelId;
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        if (IsSelectableReviewModel(catalog, selectedReviewModelId))
        {
            return selectedReviewModelId!;
        }

        var presetReviewModelId = (catalog.Presets ?? [])
            .FirstOrDefault(preset => !string.IsNullOrWhiteSpace(state.SelectedModelPresetId) &&
                preset.PresetId.Equals(state.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase))
            ?.ReviewModelId;
        return IsSelectableReviewModel(catalog, presetReviewModelId)
            ? presetReviewModelId!
            : "llm-jp-4-8b-thinking-q4-k-m";
    }

    private string ResolveReviewModelPath(string modelId)
    {
        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is not null &&
            installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
        {
            return installed.FilePath;
        }

        return paths.ReviewModelPath;
    }

    private string ResolveOutputSanitizerProfile(string modelId)
    {
        var catalogProfile = new ModelCatalogService(paths)
            .LoadBuiltInCatalog()
            .Models
            .FirstOrDefault(model => model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))
            ?.OutputSanitizerProfile;

        return LlmOutputSanitizerProfiles.ForReviewModel(modelId, catalogProfile);
    }

    private static bool IsSelectableReviewModel(ModelCatalog catalog, string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            catalog.Models.Any(model =>
                model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
                ModelCatalogCompatibility.IsSelectable(model));
    }

    private static int ResolveSummaryMaxTokens(ReviewRuntimeTuning tuning)
    {
        if (tuning.MaxTokens <= 0)
        {
            return 1024;
        }

        return Math.Clamp(tuning.MaxTokens, 512, 768);
    }
}
