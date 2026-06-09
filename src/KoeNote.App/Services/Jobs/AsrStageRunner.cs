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
            var config = CreateAsrConfig(engineId, outputDirectory);
            var result = await TranscribeWithRetryAsync(
                engine,
                new AsrInput(job.JobId, normalizedAudioPath),
                config,
                effectiveAsrSettings,
                report,
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

    private async Task<AsrResult> TranscribeWithRetryAsync(
        IAsrEngine engine,
        AsrInput input,
        AsrEngineConfig config,
        AsrSettings settings,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var profiles = ShouldUseExplicitFasterWhisperProfile(config.ModelId)
            ? AsrExecutionProfiles.BuildNativeCrashRetryLadder(settings.ExecutionProfileId)
            : [AsrExecutionProfiles.Resolve(AsrExecutionProfiles.Auto)];
        if (profiles[0].IsGpu && !AsrCudaRuntimeLayout.HasPackage(paths))
        {
            profiles = [AsrExecutionProfiles.Resolve(AsrExecutionProfiles.Auto)];
        }

        AsrWorkerException? lastException = null;

        for (var index = 0; index < profiles.Count; index++)
        {
            var profile = profiles[index];
            try
            {
                report(new JobRunUpdate(LatestLog: $"Running ASR with profile {profile.ProfileId}..."));
                return await engine.TranscribeAsync(
                    input,
                    config,
                    CreateAsrOptions(settings, profile, index + 1, ShouldUseExplicitFasterWhisperProfile(config.ModelId)),
                    cancellationToken);
            }
            catch (AsrWorkerException exception) when (
                exception.Category == AsrFailureCategory.NativeCrash &&
                profile.IsGpu &&
                index + 1 < profiles.Count)
            {
                lastException = exception;
                var nextProfile = profiles[index + 1];
                jobLogRepository.AddEvent(
                    input.JobId,
                    "asr",
                    "warning",
                    $"ASR native crash with GPU profile {profile.ProfileId}; retrying on GPU profile {nextProfile.ProfileId}. Worker log: {exception.WorkerLogPath ?? "(none)"}");
                report(new JobRunUpdate(
                    RefreshLogs: true,
                    LatestLog: $"ASR GPU profile {profile.ProfileId} crashed; retrying with {nextProfile.ProfileId}."));
            }
        }

        throw lastException ?? new AsrWorkerException(AsrFailureCategory.NativeCrash, "ASR failed before a retry result was produced.");
    }

    private static AsrOptions CreateAsrOptions(
        AsrSettings settings,
        AsrExecutionProfile profile,
        int attemptNumber,
        bool supportsExplicitFasterWhisperOptions)
    {
        var context = string.IsNullOrWhiteSpace(settings.ContextText) ? null : settings.ContextText;
        if (!supportsExplicitFasterWhisperOptions)
        {
            return new AsrOptions(settings.Hotwords, context, TimeSpan.FromHours(2));
        }

        return new AsrOptions(
            settings.Hotwords,
            context,
            TimeSpan.FromHours(2),
            profile.Device,
            profile.ComputeType,
            profile.ProfileId,
            attemptNumber,
            settings.EnableChunkedGpuAsr && profile.IsGpu ? 300 : null);
    }

    private static bool ShouldUseExplicitFasterWhisperProfile(string modelId)
    {
        return modelId.Equals("faster-whisper-large-v3-turbo", StringComparison.OrdinalIgnoreCase) ||
            modelId.Equals("faster-whisper-large-v3", StringComparison.OrdinalIgnoreCase) ||
            modelId.Equals("whisper-base", StringComparison.OrdinalIgnoreCase) ||
            modelId.Equals("whisper-small", StringComparison.OrdinalIgnoreCase);
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
