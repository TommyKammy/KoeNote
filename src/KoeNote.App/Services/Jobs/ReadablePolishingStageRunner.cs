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
    ReadablePolishingPromptSettingsRepository promptSettingsRepository) : IReadablePolishingStageRunner
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
            var profile = new LlmProfileResolver(paths, installedModelRepository).Resolve(catalog, modelId);
            var taskSettings = new LlmTaskSettingsResolver().Resolve(profile, LlmTaskKind.Polishing);
            var promptSettings = ReadablePolishingPromptSettingsResolver.Resolve(profile, promptSettingsRepository);

            jobLogRepository.AddEvent(job.JobId, "polishing", "info", LlmExecutionLogFormatter.Format(profile, taskSettings));
            jobLogRepository.AddEvent(
                job.JobId,
                "polishing",
                "info",
                $"Readable polishing prompt settings: family={promptSettings.ModelFamily}, preset={promptSettings.PresetId}, custom={promptSettings.UseCustomPrompt}");
            report(new JobRunUpdate(RefreshLogs: true));

            var result = await polishingService.PolishAsync(
                new TranscriptPolishingOptions(
                    job.JobId,
                    profile.LlamaCompletionPath,
                    profile.ModelPath,
                    outputDirectory,
                    profile.ModelId,
                    taskSettings.PromptTemplateId,
                    taskSettings.GenerationProfile,
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
                    PromptSettings: promptSettings),
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
        var selectedReviewModelId = state.SelectedReviewModelId;
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

    private static bool IsSelectableReviewModel(ModelCatalog catalog, string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            catalog.Models.Any(model =>
                model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
                ModelCatalogCompatibility.IsSelectable(model));
    }
}
