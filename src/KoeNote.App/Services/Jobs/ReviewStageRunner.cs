using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Jobs;

public sealed class ReviewStageRunner(
    AppPaths paths,
    JobRepository jobRepository,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository,
    InstalledModelRepository installedModelRepository,
    SetupStateService setupStateService,
    ReviewWorker reviewWorker) : IReviewStageRunner
{
    public async Task<bool> RunAsync(
        JobSummary job,
        IReadOnlyList<TranscriptSegment> segments,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Running, 10));
        jobRepository.MarkReviewRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true));
        stageProgressRepository.Upsert(job.JobId, "review", "running", 10, startedAt: startedAt);
        report(new JobRunUpdate(LatestLog: $"Running review for {job.FileName}"));

        try
        {
            var outputDirectory = Path.Combine(paths.Jobs, job.JobId, "review");
            var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
            var modelId = ResolveReviewModelId();
            var sanitizerProfile = ResolveOutputSanitizerProfile(modelId);
            var tuning = ReviewRuntimeTuningProfiles.ForReviewModel(modelId);
            var result = await reviewWorker.RunAsync(new ReviewRunOptions(
                job.JobId,
                ReviewRuntimeResolver.ResolveLlamaCompletionPath(paths, catalog, modelId),
                ResolveReviewModelPath(modelId),
                outputDirectory,
                segments,
                MinConfidence: 0.5,
                Timeout: tuning.Timeout,
                ModelId: modelId,
                OutputSanitizerProfile: sanitizerProfile,
                ContextSize: tuning.ContextSize,
                GpuLayers: tuning.GpuLayers,
                MaxTokens: tuning.MaxTokens,
                ChunkSegmentCount: tuning.ChunkSegmentCount,
                Threads: tuning.Threads,
                ThreadsBatch: tuning.ThreadsBatch,
                UseJsonSchema: tuning.UseJsonSchema,
                EnableRepair: tuning.EnableRepair,
                PromptProfile: tuning.PromptProfile),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Succeeded, 100, result.Duration));
            stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "succeeded",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            jobRepository.MarkReviewSucceeded(job, result.Drafts.Count);
            job.UnreviewedDrafts = result.Drafts.Count;
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "review", "info", $"Generated {result.Drafts.Count} correction drafts: {result.NormalizedDraftsPath}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"Review completed: {result.Drafts.Count} drafts"));
            report(result.Drafts.Count > 0
                ? new JobRunUpdate(Drafts: result.Drafts)
                : new JobRunUpdate(ClearReviewPreview: true));
            return true;
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Cancelled, 100, finishedAt - startedAt));
            stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            jobRepository.MarkCancelled(job, "review");
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "review", "info", "Run was cancelled.");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: "整文をキャンセルしました。"));
            return false;
        }
        catch (ReviewWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Failed, 100, finishedAt - startedAt, exception.Category.ToString()));
            stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            jobRepository.MarkReviewFailed(job, exception.Category.ToString());
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "review", "error", $"{exception.Category}: {exception.Message}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"Review failed ({exception.Category}): {exception.Message}"));
            return false;
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Failed, 100, finishedAt - startedAt, ReviewFailureCategory.Unknown.ToString()));
            stageProgressRepository.Upsert(
                job.JobId,
                "review",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: ReviewFailureCategory.Unknown.ToString());
            jobRepository.MarkReviewFailed(job, ReviewFailureCategory.Unknown.ToString());
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "review", "error", $"{ReviewFailureCategory.Unknown}: {exception.Message}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"Review failed ({ReviewFailureCategory.Unknown}): {exception.Message}"));
            return false;
        }
    }

    public void Skip(JobSummary job, Action<JobRunUpdate> report)
    {
        var now = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Skipped, 100, TimeSpan.Zero));
        stageProgressRepository.Upsert(
            job.JobId,
            "review",
            "skipped",
            100,
            now,
            now,
            0,
            errorCategory: "disabled_by_user");
        jobRepository.MarkReviewSkippedAndClearDrafts(job);
        jobLogRepository.AddEvent(job.JobId, "review", "info", "Review stage skipped by user setting.");
        report(new JobRunUpdate(
            RefreshJobViews: true,
            RefreshLogs: true,
            LatestLog: "Review stage skipped. ASR transcript is ready.",
            ClearReviewPreview: true));
    }

    private string ResolveModelPath(string modelId, string fallbackPath)
    {
        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is not null &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
        {
            return installed.FilePath;
        }

        return fallbackPath;
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
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            var installed = installedModelRepository.FindInstalledModel(modelId);
            if (installed is not null &&
                installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
            {
                return installed.FilePath;
            }
        }

        return ResolveModelPath("llm-jp-4-8b-thinking-q4-k-m", paths.ReviewModelPath);
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
}
