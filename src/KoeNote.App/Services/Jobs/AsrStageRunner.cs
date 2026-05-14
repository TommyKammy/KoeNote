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
        var outputDirectory = Path.Combine(paths.Jobs, job.JobId, "asr");
        var activeStage = "asr";
        report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Running, JobRunProgressPlan.AsrRunning));
        jobRepository.MarkAsrRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true));
        stageProgressRepository.Upsert(job.JobId, "asr", "running", JobRunProgressPlan.AsrRunning, startedAt: startedAt);
        report(new JobRunUpdate(LatestLog: $"Running ASR for {job.FileName}"));

        try
        {
            var effectiveAsrSettings = correctionMemoryService.EnrichAsrSettings(asrSettings);
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

            jobRepository.MarkAsrSucceeded(job);
            report(new JobRunUpdate(RefreshJobViews: true));
            report(new JobRunUpdate(LatestLog: "Running speaker diarization..."));
            activeStage = "diarization";
            jobRepository.MarkDiarizationRunning(job);
            report(new JobRunUpdate(RefreshJobViews: true));
            var diarizationResult = await diarizationService.RunAsync(
                job.JobId,
                normalizedAudioPath,
                result.Segments,
                cancellationToken);
            var segments = diarizationResult.Segments;

            LogDiarizationResult(job, diarizationResult);
            jobRepository.MarkDiarizationSucceeded(job);
            report(new JobRunUpdate(RefreshJobViews: true));

            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(
                JobRunStage.Asr,
                JobRunStageState.Succeeded,
                JobRunProgressPlan.Completed,
                result.Duration,
                Segments: segments));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "succeeded",
                JobRunProgressPlan.Completed,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds,
                logPath: result.RawOutputPath);
            jobLogRepository.AddEvent(job.JobId, "asr", "info", $"Generated {segments.Count} ASR segments: {result.NormalizedSegmentsPath}");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"ASR completed: {segments.Count} segments"));
            return segments;
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Cancelled, JobRunProgressPlan.Completed, finishedAt - startedAt));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "cancelled",
                JobRunProgressPlan.Completed,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            jobRepository.MarkCancelled(job, activeStage);
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(job.JobId, "asr", "info", "Run was cancelled.");
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: "ASRをキャンセルしました。"));
            return null;
        }
        catch (AsrWorkerException exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Failed, JobRunProgressPlan.Completed, finishedAt - startedAt, exception.Category.ToString()));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                JobRunProgressPlan.Completed,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: exception.Category.ToString());
            jobRepository.MarkAsrFailed(job, exception.Category.ToString());
            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(
                job.JobId,
                "asr",
                "error",
                JobLogDiagnostics.FormatException(exception.Category.ToString(), exception, outputDirectory));
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: $"ASR failed ({exception.Category}): {exception.Message}"));
            return null;
        }
        catch (Exception exception)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(JobRunStage.Asr, JobRunStageState.Failed, JobRunProgressPlan.Completed, finishedAt - startedAt, AsrFailureCategory.Unknown.ToString()));
            stageProgressRepository.Upsert(
                job.JobId,
                "asr",
                "failed",
                JobRunProgressPlan.Completed,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: AsrFailureCategory.Unknown.ToString());
            if (string.Equals(activeStage, "diarization", StringComparison.Ordinal))
            {
                jobRepository.MarkDiarizationFailed(job, AsrFailureCategory.Unknown.ToString());
            }
            else
            {
                jobRepository.MarkAsrFailed(job, AsrFailureCategory.Unknown.ToString());
            }

            report(new JobRunUpdate(RefreshJobViews: true));
            jobLogRepository.AddEvent(
                job.JobId,
                "asr",
                "error",
                JobLogDiagnostics.FormatException(AsrFailureCategory.Unknown.ToString(), exception, outputDirectory));
            var latestLog = string.Equals(activeStage, "diarization", StringComparison.Ordinal)
                ? $"Speaker diarization failed ({AsrFailureCategory.Unknown}): {exception.Message}"
                : $"ASR failed ({AsrFailureCategory.Unknown}): {exception.Message}";
            report(new JobRunUpdate(RefreshLogs: true, LatestLog: latestLog));
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
                paths.AsrPythonPath,
                ResolveModelPath("kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath),
                outputDirectory,
                "kotoba-whisper-v2.2-faster",
                paths.FasterWhisperScriptPath,
                "kotoba-whisper-v2.2"),
            "whisper-base" => new AsrEngineConfig(
                paths.AsrPythonPath,
                ResolveModelPath("whisper-base", paths.WhisperBaseModelPath),
                outputDirectory,
                "whisper-base",
                paths.FasterWhisperScriptPath,
                "base"),
            "whisper-small" => new AsrEngineConfig(
                paths.AsrPythonPath,
                ResolveModelPath("whisper-small", paths.WhisperSmallModelPath),
                outputDirectory,
                "whisper-small",
                paths.FasterWhisperScriptPath,
                "small"),
            "faster-whisper-large-v3-turbo" => new AsrEngineConfig(
                paths.AsrPythonPath,
                ResolveModelPath("faster-whisper-large-v3-turbo", paths.FasterWhisperModelPath),
                outputDirectory,
                "faster-whisper-large-v3-turbo",
                paths.FasterWhisperScriptPath,
                "large-v3-turbo"),
            "faster-whisper-large-v3" => new AsrEngineConfig(
                paths.AsrPythonPath,
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
                paths.AsrPythonPath,
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
