using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Llm;
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
    TranscriptSummaryService summaryService,
    ISetupHostResourceProbe? hostResourceProbe = null) : ISummaryStageRunner
{
    public async Task RunAsync(
        JobSummary job,
        Action<JobRunUpdate> report,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.Now;
        var outputDirectory = Path.Combine(paths.Jobs, job.JobId, "summary");
        report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Running, JobRunProgressPlan.SummaryRunning));
        jobRepository.MarkSummaryRunning(job);
        report(new JobRunUpdate(RefreshJobViews: true));
        stageProgressRepository.Upsert(job.JobId, "summary", "running", JobRunProgressPlan.SummaryRunning, startedAt: startedAt);
        report(new JobRunUpdate(LatestLog: $"Running summary for {job.FileName}"));

        try
        {
            var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
            var modelId = ResolveReviewModelId();
            var profile = new LlmProfileResolver(paths, installedModelRepository).Resolve(catalog, modelId);
            LlmGpuRuntimeGuard.ThrowIfRequiredRuntimeMissing(paths, hostResourceProbe, profile);
            var taskSettings = new LlmTaskSettingsResolver().Resolve(profile, LlmTaskKind.Summary);
            var useMtpServer = TryResolveGemma12BMtpServer(
                profile,
                out var llamaServerPath,
                out var mtpDraftModelPath);
            var generationProfile = useMtpServer
                ? $"{taskSettings.GenerationProfile}; runtime=llama-server-chat-mtp"
                : taskSettings.GenerationProfile;
            jobLogRepository.AddEvent(job.JobId, "summary", "info", LlmExecutionLogFormatter.Format(profile, taskSettings));
            if (useMtpServer)
            {
                jobLogRepository.AddEvent(
                    job.JobId,
                    "summary",
                    "info",
                    $"Gemma 4 12B MTP server runtime enabled for summary: server=\"{llamaServerPath}\" draft=\"{mtpDraftModelPath}\"");
            }

            var result = await summaryService.SummarizeAsync(new TranscriptSummaryOptions(
                job.JobId,
                profile.LlamaCompletionPath,
                profile.ModelPath,
                outputDirectory,
                profile.ModelId,
                generationProfile,
                taskSettings.PromptVersion,
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
                PromptTemplateId: taskSettings.PromptTemplateId,
                ValidationMode: taskSettings.ValidationMode,
                MaxAttempts: ResolveSummaryMaxAttempts(profile.ModelId, profile.ModelFamily),
                RuntimeEnvironment: LlamaRuntimeEnvironment.Build(paths),
                UseLlamaServerChatMtp: useMtpServer,
                LlamaServerPath: llamaServerPath,
                MtpDraftModelPath: mtpDraftModelPath),
                cancellationToken);

            var finishedAt = DateTimeOffset.Now;
            var status = string.IsNullOrWhiteSpace(result.Content) ? "failed" : "succeeded";
            report(new JobRunUpdate(
                JobRunStage.Summary,
                status == "succeeded" ? JobRunStageState.Succeeded : JobRunStageState.Failed,
                status == "succeeded" ? JobRunProgressPlan.Completed : JobRunProgressPlan.SummaryFailed,
                result.Duration,
                status == "succeeded" ? null : "summary_failed"));
            stageProgressRepository.Upsert(
                job.JobId,
                "summary",
                status,
                status == "succeeded" ? JobRunProgressPlan.Completed : JobRunProgressPlan.SummaryFailed,
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
            report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Cancelled, JobRunProgressPlan.Completed, finishedAt - startedAt));
            stageProgressRepository.Upsert(
                job.JobId,
                "summary",
                "cancelled",
                JobRunProgressPlan.Completed,
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
            MarkFailed(job, report, startedAt, exception.Category.ToString(), exception, outputDirectory);
        }
        catch (Exception exception)
        {
            MarkFailed(job, report, startedAt, ReviewFailureCategory.Unknown.ToString(), exception, outputDirectory);
        }
    }

    public void Skip(JobSummary job, Action<JobRunUpdate> report, string reason)
    {
        var now = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Skipped, JobRunProgressPlan.Completed, TimeSpan.Zero));
        stageProgressRepository.Upsert(
            job.JobId,
            "summary",
            "skipped",
            JobRunProgressPlan.Completed,
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
        Exception exception,
        string outputDirectory)
    {
        var finishedAt = DateTimeOffset.Now;
        report(new JobRunUpdate(JobRunStage.Summary, JobRunStageState.Failed, JobRunProgressPlan.SummaryFailed, finishedAt - startedAt, errorCategory));
        stageProgressRepository.Upsert(
            job.JobId,
            "summary",
            "failed",
            JobRunProgressPlan.SummaryFailed,
            startedAt,
            finishedAt,
            (finishedAt - startedAt).TotalSeconds,
            errorCategory: errorCategory);
        jobRepository.MarkSummaryFailed(job, errorCategory);
        jobLogRepository.AddEvent(
            job.JobId,
            "summary",
            "error",
            JobLogDiagnostics.FormatException(errorCategory, exception, outputDirectory));
        report(new JobRunUpdate(RefreshJobViews: true, RefreshLogs: true, LatestLog: $"Summary failed ({errorCategory}): {exception.Message}"));
    }

    private string ResolveReviewModelId()
    {
        var state = setupStateService.Load();
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var selectedModelId = ReviewModelSelectionResolver.Resolve(catalog, state.SelectedReviewModelId, state.SelectedModelPresetId);
        return Gemma12BLocalValidation.IsTargetModel(selectedModelId) &&
            Gemma12BLocalValidation.IsMtpServerEnabled()
            ? selectedModelId
            : DirectLlmStageModelResolver.Resolve(catalog, state.SelectedReviewModelId, state.SelectedModelPresetId);
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

    internal string ResolveMtpDraftModelPath()
    {
        var configuredDraft = Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath();
        if (configuredDraft is not null)
        {
            return configuredDraft;
        }

        var installedDraft = installedModelRepository.FindInstalledModel(Gemma12BLocalValidation.MtpDraftModelId);
        if (installedDraft is not null &&
            installedDraft.Role.Equals("review_aux", StringComparison.OrdinalIgnoreCase) &&
            installedDraft.Verified &&
            File.Exists(installedDraft.FilePath) &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installedDraft.FilePath))
        {
            return installedDraft.FilePath;
        }

        var storageRoot = setupStateService.Load().StorageRoot;
        return string.IsNullOrWhiteSpace(storageRoot)
            ? Gemma12BLocalValidation.ResolveMtpDraftModelPath()
            : Gemma12BLocalValidation.ResolveMtpDraftModelPath(storageRoot);
    }

    private static int ResolveSummaryMaxAttempts(string modelId, string? modelFamily)
    {
        return modelId.Contains("bonsai", StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(modelFamily) && modelFamily.Contains("bonsai", StringComparison.OrdinalIgnoreCase))
            ? 3
            : 2;
    }
}
