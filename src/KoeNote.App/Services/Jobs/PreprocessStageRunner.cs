using KoeNote.App.Models;
using KoeNote.App.Services.Audio;

namespace KoeNote.App.Services.Jobs;

public sealed class PreprocessStageRunner(
    AppPaths paths,
    JobRepository jobRepository,
    JobLogRepository jobLogRepository,
    AudioPreprocessWorker audioPreprocessWorker) : IPreprocessStageRunner
{
    public async Task<string?> RunAsync(
        JobSummary job,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Running, 10));
        jobRepository.MarkPreprocessRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true, LatestLog: $"Running ffmpeg for {job.FileName}"));

        try
        {
            var result = await audioPreprocessWorker.NormalizeAsync(job, paths.FfmpegPath, paths, cancellationToken);
            report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Succeeded, 100, result.Duration));
            jobRepository.MarkPreprocessSucceeded(job, result.NormalizedAudioPath);
            report(new JobRunUpdate(RefreshJobViews: true, LatestLog: $"Generated normalized WAV: {result.NormalizedAudioPath}"));
            return result.NormalizedAudioPath;
        }
        catch (OperationCanceledException)
        {
            report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Cancelled, 100));
            jobRepository.MarkCancelled(job, "preprocess");
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "preprocess", "info", "Run was cancelled.");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: "実行をキャンセルしました。"));
            return null;
        }
        catch (Exception exception)
        {
            report(new JobRunUpdate(JobRunStage.Preprocess, JobRunStageState.Failed, 100));
            jobRepository.MarkPreprocessFailed(job, "ffmpeg_failed");
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "preprocess", "error", exception.Message);
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: exception.Message));
            return null;
        }
    }
}
