using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Jobs;

public sealed class AsrStageRunner(
    AppPaths paths,
    JobRepository jobRepository,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository,
    AsrEngineRegistry asrEngineRegistry,
    InstalledModelRepository installedModelRepository,
    ScriptedDiarizationService diarizationService,
    CorrectionMemoryService correctionMemoryService) : IAsrStageRunner
{
    public async Task<IReadOnlyList<TranscriptSegment>?> RunAsync(
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
            var engineId = asrEngineRegistry.Contains(effectiveAsrSettings.EngineId)
                ? effectiveAsrSettings.EngineId
                : "faster-whisper-large-v3-turbo";
            var engine = asrEngineRegistry.GetRequired(engineId);
            var result = await engine.TranscribeAsync(
                new AsrInput(job.JobId, normalizedAudioPath),
                CreateAsrConfig(engineId, outputDirectory),
                new AsrOptions(
                    effectiveAsrSettings.Hotwords,
                    string.IsNullOrWhiteSpace(effectiveAsrSettings.ContextText) ? null : effectiveAsrSettings.ContextText,
                    TimeSpan.FromHours(2)),
                cancellationToken);

            report(new JobRunUpdate(LatestLog: "Running speaker diarization..."));
            var diarizationResult = await diarizationService.RunAsync(
                job.JobId,
                normalizedAudioPath,
                result.Segments,
                cancellationToken);
            var segments = diarizationResult.Segments;

            LogDiarizationResult(job, diarizationResult);

            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(
                JobRunStage.Asr,
                JobRunStageState.Succeeded,
                100,
                result.Duration,
                Segments: segments));
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
            jobLogRepository.AddEvent(job.JobId, "asr", "info", $"Generated {segments.Count} ASR segments: {result.NormalizedSegmentsPath}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"ASR completed: {segments.Count} segments"));
            return segments;
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Cancelled, 100, finishedAt - startedAt));
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
            return null;
        }
        catch (AsrWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Failed, 100, finishedAt - startedAt, exception.Category.ToString()));
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
            return null;
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Failed, 100, finishedAt - startedAt, AsrFailureCategory.Unknown.ToString()));
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
            return null;
        }
    }

    private void LogDiarizationResult(JobSummary job, DiarizationRunResult diarizationResult)
    {
        if (diarizationResult.AssignedSegmentCount > 0)
        {
            jobLogRepository.AddEvent(
                job.JobId,
                "diarization",
                "info",
                $"Assigned {diarizationResult.SpeakerCount} speakers to {diarizationResult.AssignedSegmentCount} segments with diarize: {diarizationResult.RawOutputPath}");
            return;
        }

        jobLogRepository.AddEvent(
            job.JobId,
            "diarization",
            "warning",
            $"Speaker diarization skipped: {diarizationResult.Status}");
    }

    private AsrEngineConfig CreateAsrConfig(string engineId, string outputDirectory)
    {
        return engineId switch
        {
            "kotoba-whisper-v2.2-faster" => new AsrEngineConfig(
                "python",
                ResolveModelPath("kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath),
                outputDirectory,
                "kotoba-whisper-v2.2-faster",
                paths.FasterWhisperScriptPath,
                "kotoba-whisper-v2.2"),
            "whisper-base" => new AsrEngineConfig(
                "python",
                ResolveModelPath("whisper-base", paths.WhisperBaseModelPath),
                outputDirectory,
                "whisper-base",
                paths.FasterWhisperScriptPath,
                "base"),
            "faster-whisper-large-v3-turbo" => new AsrEngineConfig(
                "python",
                ResolveModelPath("faster-whisper-large-v3-turbo", paths.FasterWhisperModelPath),
                outputDirectory,
                "faster-whisper-large-v3-turbo",
                paths.FasterWhisperScriptPath,
                "large-v3-turbo"),
            "faster-whisper-large-v3" => new AsrEngineConfig(
                "python",
                ResolveModelPath("faster-whisper-large-v3", paths.FasterWhisperLargeV3ModelPath),
                outputDirectory,
                "faster-whisper-large-v3",
                paths.FasterWhisperScriptPath,
                "large-v3"),
            "reazonspeech-k2-v3" => new AsrEngineConfig(
                "python",
                ResolveModelPath("reazonspeech-k2-v3-ja", paths.ReazonSpeechK2ModelPath),
                outputDirectory,
                "reazonspeech-k2-v3",
                paths.ReazonSpeechK2ScriptPath,
                "v3-k2"),
            _ => new AsrEngineConfig(
                "python",
                ResolveModelPath("faster-whisper-large-v3-turbo", paths.FasterWhisperModelPath),
                outputDirectory,
                "faster-whisper-large-v3-turbo",
                paths.FasterWhisperScriptPath,
                "large-v3-turbo")
        };
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
