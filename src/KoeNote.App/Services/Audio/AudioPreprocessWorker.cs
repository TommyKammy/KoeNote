using KoeNote.App.Models;
using KoeNote.App.Services.Jobs;
using System.IO;

namespace KoeNote.App.Services.Audio;

public sealed class AudioPreprocessWorker(
    ExternalProcessRunner processRunner,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository)
{
    public async Task<AudioPreprocessResult> NormalizeAsync(
        JobSummary job,
        string ffmpegPath,
        AppPaths paths,
        CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        var normalizedDirectory = Path.Combine(paths.Jobs, job.JobId, "normalized");
        Directory.CreateDirectory(normalizedDirectory);

        var normalizedAudioPath = Path.Combine(normalizedDirectory, "audio.wav");
        stageProgressRepository.Upsert(job.JobId, "preprocess", "running", 10, startedAt: startedAt);

        ProcessRunResult result;
        try
        {
            result = await processRunner.RunAsync(
                ffmpegPath,
                new[] { "-y", "-i", job.SourceAudioPath, "-ar", "24000", "-ac", "1", normalizedAudioPath },
                TimeSpan.FromMinutes(30),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var failedAt = DateTimeOffset.Now;
            stageProgressRepository.Upsert(
                job.JobId,
                "preprocess",
                "cancelled",
                100,
                startedAt,
                failedAt,
                (failedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            throw;
        }
        catch (Exception)
        {
            var failedAt = DateTimeOffset.Now;
            stageProgressRepository.Upsert(
                job.JobId,
                "preprocess",
                "failed",
                100,
                startedAt,
                failedAt,
                (failedAt - startedAt).TotalSeconds,
                errorCategory: "ffmpeg_start_failed");
            throw;
        }

        var logPath = jobLogRepository.SaveWorkerLog(job.JobId, "preprocess", result.StandardOutput, result.StandardError);
        var finishedAt = DateTimeOffset.Now;

        if (result.ExitCode != 0)
        {
            stageProgressRepository.Upsert(
                job.JobId,
                "preprocess",
                "failed",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                result.ExitCode,
                "ffmpeg_failed",
                logPath);

            throw new InvalidOperationException($"ffmpeg exited with code {result.ExitCode}. See log: {logPath}");
        }

        if (!File.Exists(normalizedAudioPath))
        {
            stageProgressRepository.Upsert(
                job.JobId,
                "preprocess",
                "failed",
                100,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                result.ExitCode,
                "ffmpeg_output_missing",
                logPath);

            throw new FileNotFoundException($"ffmpeg succeeded but did not create the normalized WAV. See log: {logPath}", normalizedAudioPath);
        }

        stageProgressRepository.Upsert(
            job.JobId,
            "preprocess",
            "succeeded",
            100,
            startedAt,
            finishedAt,
            result.Duration.TotalSeconds,
            result.ExitCode,
            logPath: logPath);

        return new AudioPreprocessResult(normalizedAudioPath, logPath, result.Duration, result.ExitCode);
    }
}
