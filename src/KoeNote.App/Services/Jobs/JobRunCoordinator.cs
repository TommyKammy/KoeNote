using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Jobs;

public sealed class JobRunCoordinator(
    AppPaths paths,
    JobRepository jobRepository,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository,
    AudioPreprocessWorker audioPreprocessWorker,
    AsrWorker asrWorker,
    ReviewWorker reviewWorker,
    CorrectionMemoryService correctionMemoryService)
{
    public async Task RunAsync(
        JobSummary job,
        AsrSettings asrSettings,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Running, 10));
        jobRepository.MarkPreprocessRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true, LatestLog: $"Running ffmpeg for {job.FileName}"));

        try
        {
            var result = await audioPreprocessWorker.NormalizeAsync(job, paths.FfmpegPath, paths, cancellationToken);
            report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Succeeded, 100));
            jobRepository.MarkPreprocessSucceeded(job, result.NormalizedAudioPath);
            report(new JobRunUpdate(RefreshJobViews: true, LatestLog: $"Generated normalized WAV: {result.NormalizedAudioPath}"));

            await RunAsrAsync(job, result.NormalizedAudioPath, asrSettings, report, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Cancelled, 100));
            jobRepository.MarkCancelled(job, "preprocess");
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "preprocess", "info", "Run was cancelled.");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: "実行をキャンセルしました。"));
        }
        catch (Exception exception)
        {
            report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Failed, 100));
            jobRepository.MarkPreprocessFailed(job, "ffmpeg_failed");
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "preprocess", "error", exception.Message);
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: exception.Message));
        }
    }

    private async Task RunAsrAsync(
        JobSummary job,
        string normalizedAudioPath,
        AsrSettings asrSettings,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Running, 10));
        jobRepository.MarkAsrRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true));
        stageProgressRepository.Upsert(job.JobId, "asr", "running", 10, startedAt: startedAt);
        report(new JobRunUpdate(LatestLog: $"Running ASR for {job.FileName}"));

        try
        {
            var effectiveAsrSettings = correctionMemoryService.EnrichAsrSettings(asrSettings);
            var outputDirectory = Path.Combine(paths.Jobs, job.JobId, "asr");
            var result = await asrWorker.RunAsync(new AsrRunOptions(
                job.JobId,
                normalizedAudioPath,
                paths.CrispAsrPath,
                paths.VibeVoiceAsrModelPath,
                outputDirectory,
                effectiveAsrSettings.Hotwords,
                string.IsNullOrWhiteSpace(effectiveAsrSettings.ContextText) ? null : effectiveAsrSettings.ContextText,
                Timeout: TimeSpan.FromHours(2)),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(
                JobRunStage.Asr,
                JobRunStageState.Succeeded,
                100,
                Segments: result.Segments));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "succeeded",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            jobRepository.MarkAsrSucceeded(job);
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "asr", "info", $"Generated {result.Segments.Count} ASR segments: {result.NormalizedSegmentsPath}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"ASR completed: {result.Segments.Count} segments"));

            await RunReviewAsync(job, result.Segments, report, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Cancelled, 100));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "cancelled",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            jobRepository.MarkCancelled(job, "asr");
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "asr", "info", "Run was cancelled.");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: "ASRをキャンセルしました。"));
        }
        catch (AsrWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Failed, 100, exception.Category.ToString()));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            jobRepository.MarkAsrFailed(job, exception.Category.ToString());
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{exception.Category}: {exception.Message}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"ASR failed ({exception.Category}): {exception.Message}"));
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Failed, 100, AsrFailureCategory.Unknown.ToString()));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                100,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: AsrFailureCategory.Unknown.ToString());
            jobRepository.MarkAsrFailed(job, AsrFailureCategory.Unknown.ToString());
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "asr", "error", $"{AsrFailureCategory.Unknown}: {exception.Message}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"ASR failed ({AsrFailureCategory.Unknown}): {exception.Message}"));
        }
    }

    private async Task RunReviewAsync(
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
                paths.ReviewModelPath,
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
}
