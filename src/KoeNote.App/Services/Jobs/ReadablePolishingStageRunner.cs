using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Jobs;

public sealed class ReadablePolishingStageRunner(
    AppPaths paths,
    JobRepository jobRepository,
    StageProgressRepository stageProgressRepository,
    JobLogRepository jobLogRepository,
    InstalledModelRepository installedModelRepository,
    SetupStateService setupStateService,
    TranscriptPolishingService polishingService,
    ReadablePolishingPromptSettingsRepository promptSettingsRepository,
    ISetupHostResourceProbe? hostResourceProbe = null) : IReadablePolishingStageRunner
{
    public async Task<ReadablePolishingStageResult> RunAsync(
        JobSummary job,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var outputDirectory = Path.Combine(paths.Jobs, job.JobId, "polishing");
        report(new JobRunUpdate(
            JobRunStage.Review,
            JobRunStageState.Running,
            JobRunProgressPlan.ReadablePolishingRunning,
            StageStatusText: "完成文書作成中"));
        jobRepository.MarkReadablePolishingRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true));
        stageProgressRepository.Upsert(job.JobId, "polishing", "running", JobRunProgressPlan.ReadablePolishingRunning, startedAt: startedAt);
        report(new JobRunUpdate(LatestLog: "整文を生成しています。"));

        try
        {
            var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
            var modelId = ResolveReviewModelId(catalog);
            var profileResolver = new LlmProfileResolver(paths, installedModelRepository);
            var profile = profileResolver.Resolve(catalog, modelId);
            LlmGpuRuntimeGuard.ThrowIfRequiredRuntimeMissing(paths, hostResourceProbe, profile);
            var taskSettings = new LlmTaskSettingsResolver().Resolve(profile, LlmTaskKind.Polishing);
            var promptSettings = ReadablePolishingPromptSettingsResolver.Resolve(profile, promptSettingsRepository);
            var fallbackOptions = BuildGemma12BChunkFallbackOptions(
                profileResolver,
                catalog,
                profile,
                job.JobId,
                outputDirectory,
                promptSettings);
            if (fallbackOptions is not null)
            {
                jobLogRepository.AddEvent(
                    job.JobId,
                    "polishing",
                    "info",
                    $"Gemma 4 12B local validation fallback enabled: {fallbackOptions.ModelId}");
            }

            var polishingOptions = BuildPolishingOptions(
                job.JobId,
                profile,
                taskSettings,
                promptSettings,
                outputDirectory,
                fallbackOptions);
            if (polishingOptions.UseLlamaServerChatMtp)
            {
                jobLogRepository.AddEvent(
                    job.JobId,
                    "polishing",
                    "info",
                    $"Gemma 4 12B MTP server runtime enabled: server=\"{polishingOptions.LlamaServerPath}\" draft=\"{polishingOptions.MtpDraftModelPath}\" draft_tokens={polishingOptions.MtpDraftTokens}");
            }

            jobLogRepository.AddEvent(job.JobId, "polishing", "info", LlmExecutionLogFormatter.Format(profile, taskSettings));
            jobLogRepository.AddEvent(
                job.JobId,
                "polishing",
                "info",
                $"Readable polishing prompt settings: family={promptSettings.ModelFamily}, preset={promptSettings.PresetId}, custom={promptSettings.UseCustomPrompt}");
            report(new JobRunUpdate(RefreshLogs: true));

            var result = await polishingService.PolishAsync(
                polishingOptions,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(result.Content))
            {
                var message = "整文を作成できませんでした。素起こしを確認してから再生成してください。";
                jobLogRepository.AddEvent(job.JobId, "polishing", "error", $"Readable polishing failed derivative: {result.DerivativeId}");
                MarkFailed(job, report, startedAt, result.Duration, "empty_output", message);
                return new ReadablePolishingStageResult(false, false, message);
            }

            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Succeeded,
                JobRunProgressPlan.Completed,
                result.Duration,
                StageStatusText: "完了"));
            stageProgressRepository.Upsert(
                job.JobId,
                "polishing",
                "succeeded",
                JobRunProgressPlan.Completed,
                startedAt,
                finishedAt,
                result.Duration.TotalSeconds);
            jobRepository.MarkReadablePolishingSucceeded(job);
            jobLogRepository.AddEvent(job.JobId, "polishing", "info", $"Generated readable polished derivative: {result.DerivativeId}");
            var successMessage = "整文が完了しました。";
            report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: successMessage));
            return new ReadablePolishingStageResult(true, false, successMessage);
        }
        catch (OperationCanceledException)
        {
            var finishedAt = DateTimeOffset.Now;
            report(new JobRunUpdate(
                JobRunStage.Review,
                JobRunStageState.Cancelled,
                JobRunProgressPlan.Completed,
                finishedAt - startedAt,
                StageStatusText: "中止"));
            stageProgressRepository.Upsert(
                job.JobId,
                "polishing",
                "cancelled",
                JobRunProgressPlan.Completed,
                startedAt,
                finishedAt,
                (finishedAt - startedAt).TotalSeconds,
                errorCategory: "cancelled");
            jobRepository.MarkReadablePolishingCancelled(job);
            jobLogRepository.AddEvent(job.JobId, "polishing", "info", "Readable polishing was cancelled.");
            var message = "整文を中止しました。";
            report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: message));
            return new ReadablePolishingStageResult(false, true, message);
        }
        catch (ReviewWorkerException exception)
        {
            var message = $"整文を作成できませんでした ({exception.Category}): {exception.Message}";
            MarkFailed(job, report, startedAt, DateTimeOffset.Now - startedAt, exception.Category.ToString(), message, exception);
            return new ReadablePolishingStageResult(false, false, message);
        }
        catch (Exception exception)
        {
            var message = $"整文を作成できませんでした: {exception.Message}";
            MarkFailed(job, report, startedAt, DateTimeOffset.Now - startedAt, ReviewFailureCategory.Unknown.ToString(), message, exception);
            return new ReadablePolishingStageResult(false, false, message);
        }
    }

    private void MarkFailed(
        JobSummary job,
        Action<JobRunUpdate> report,
        DateTimeOffset startedAt,
        TimeSpan duration,
        string errorCategory,
        string message,
        Exception? exception = null)
    {
        var finishedAt = DateTimeOffset.Now;
        report(new JobRunUpdate(
            JobRunStage.Review,
            JobRunStageState.Failed,
            JobRunProgressPlan.ReadablePolishingFailed,
            duration,
            errorCategory,
            StageStatusText: "失敗"));
        stageProgressRepository.Upsert(
            job.JobId,
            "polishing",
            "failed",
            JobRunProgressPlan.ReadablePolishingFailed,
            startedAt,
            finishedAt,
            duration.TotalSeconds,
            errorCategory: errorCategory);
        jobRepository.MarkReadablePolishingFailed(job, errorCategory);
        if (exception is not null)
        {
            var outputDirectory = Path.Combine(paths.Jobs, job.JobId, "polishing");
            jobLogRepository.AddEvent(
                job.JobId,
                "polishing",
                "error",
                JobLogDiagnostics.FormatException(errorCategory, exception, outputDirectory));
        }

        report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: message));
    }

    private string ResolveReviewModelId(ModelCatalog catalog)
    {
        var state = setupStateService.Load();
        return ReviewModelSelectionResolver.Resolve(catalog, state.SelectedReviewModelId, state.SelectedModelPresetId);
    }

    private TranscriptPolishingOptions? BuildGemma12BChunkFallbackOptions(
        LlmProfileResolver profileResolver,
        ModelCatalog catalog,
        LlmRuntimeProfile primaryProfile,
        string jobId,
        string outputDirectory,
        ReadablePolishingPromptSettings promptSettings)
    {
        if (!Gemma12BLocalValidation.IsTargetModel(primaryProfile.ModelId))
        {
            return null;
        }

        var fallbackProfile = profileResolver.Resolve(catalog, ReviewModelSelectionResolver.DefaultReviewModelId);
        if (!File.Exists(fallbackProfile.ModelPath))
        {
            return null;
        }

        var fallbackTaskSettings = new LlmTaskSettingsResolver().Resolve(fallbackProfile, LlmTaskKind.Polishing);
        return BuildPolishingOptions(
            jobId,
            fallbackProfile,
            fallbackTaskSettings,
            promptSettings,
            Path.Combine(outputDirectory, "fallback-e4b"),
            fallbackOptions: null);
    }

    private TranscriptPolishingOptions BuildPolishingOptions(
        string jobId,
        LlmRuntimeProfile profile,
        LlmTaskSettings taskSettings,
        ReadablePolishingPromptSettings promptSettings,
        string outputDirectory,
        TranscriptPolishingOptions? fallbackOptions)
    {
        var useMtpServer = TryResolveGemma12BMtpServer(
            profile,
            out var llamaServerPath,
            out var mtpDraftModelPath);
        var generationProfile = useMtpServer
            ? $"{taskSettings.GenerationProfile}; runtime=llama-server-chat-mtp"
            : taskSettings.GenerationProfile;

        return new TranscriptPolishingOptions(
            jobId,
            profile.LlamaCompletionPath,
            profile.ModelPath,
            outputDirectory,
            profile.ModelId,
            taskSettings.PromptTemplateId,
            generationProfile,
            promptSettings.PromptVersion,
            ChunkSegmentCount: taskSettings.ChunkSegmentCount,
            Timeout: profile.Timeout,
            OutputSanitizerProfile: profile.OutputSanitizerProfile,
            ContextSize: profile.ContextSize,
            GpuLayers: profile.GpuLayers,
            MaxTokens: taskSettings.MaxTokens,
            Temperature: taskSettings.Temperature,
            TopP: taskSettings.TopP,
            TopK: taskSettings.TopK,
            RepeatPenalty: taskSettings.RepeatPenalty,
            NoConversation: profile.NoConversation,
            Threads: profile.Threads,
            ThreadsBatch: profile.ThreadsBatch,
            PromptSettings: promptSettings,
            RuntimeEnvironment: LlamaRuntimeEnvironment.Build(paths),
            ChunkFallbackOptions: fallbackOptions,
            UseLlamaServerChatMtp: useMtpServer,
            LlamaServerPath: llamaServerPath,
            MtpDraftModelPath: mtpDraftModelPath);
    }

    private bool TryResolveGemma12BMtpServer(
        LlmRuntimeProfile profile,
        out string? llamaServerPath,
        out string? mtpDraftModelPath)
    {
        llamaServerPath = null;
        mtpDraftModelPath = null;

        if (!Gemma12BLocalValidation.IsTargetModel(profile.ModelId) ||
            !Gemma12BLocalValidation.IsMtpServerEnabled())
        {
            return false;
        }

        llamaServerPath = Gemma12BLocalValidation.ResolveLlamaServerPath(profile.LlamaCompletionPath);
        mtpDraftModelPath = ResolveMtpDraftModelPath();
        return true;
    }

    private string ResolveMtpDraftModelPath()
    {
        var configuredDraft = Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath();
        if (configuredDraft is not null)
        {
            return configuredDraft;
        }

        var installedDraft = installedModelRepository.FindInstalledModel(Gemma12BLocalValidation.MtpDraftModelId);
        if (installedDraft is not null &&
            installedDraft.Verified &&
            File.Exists(installedDraft.FilePath))
        {
            return installedDraft.FilePath;
        }

        var storageRoot = setupStateService.Load().StorageRoot;
        return string.IsNullOrWhiteSpace(storageRoot)
            ? Gemma12BLocalValidation.ResolveMtpDraftModelPath()
            : Gemma12BLocalValidation.ResolveMtpDraftModelPath(storageRoot);
    }
}
