using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Jobs;

public sealed class ReviewStageRunner(
    AppPaths paths,
    JobRepository jobRepository,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository,
    InstalledModelRepository installedModelRepository,
    ReviewWorker reviewWorker) : IReviewStageRunner
{
    public async Task RunAsync(
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
            var result = await reviewWorker.RunAsync(new ReviewRunOptions(
                job.JobId,
                paths.LlamaCompletionPath,
                ResolveModelPath("llm-jp-4-8b-thinking-q4-k-m", paths.ReviewModelPath),
                outputDirectory,
                segments,
                MinConfidence: 0.5,
                Timeout: TimeSpan.FromHours(2)),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Succeeded, 100));
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
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Cancelled, 100));
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
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: "推敲をキャンセルしました。"));
        }
        catch (ReviewWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Failed, 100, exception.Category.ToString()));
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
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Failed, 100, ReviewFailureCategory.Unknown.ToString()));
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
        }
    }

    public void Skip(JobSummary job, Action<JobRunUpdate> report)
    {
        var now = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Review, JobRunStageState.Skipped, 100));
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
}
