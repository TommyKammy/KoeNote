using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task RequestPostProcessAsync(PostProcessMode mode)
    {
        return RunPostProcessAsync(mode);
    }

    private async Task RunPostProcessAsync(PostProcessMode mode)
    {
        LastRequestedPostProcessMode = mode;
        if (SelectedJob is null || IsRunInProgress || IsPostProcessInProgress)
        {
            return;
        }

        if ((mode == PostProcessMode.ReviewOnly || mode == PostProcessMode.ReviewAndSummary) && !ReviewStageAssetsReady)
        {
            LatestLog = "整文ランタイムまたは整文モデルが不足しているため、後から整文を実行できません。";
            IsSetupWizardModalOpen = true;
            return;
        }

        if ((mode == PostProcessMode.SummaryOnly || mode == PostProcessMode.ReviewAndSummary) && !SummaryStageAssetsReady)
        {
            LatestLog = "要約ランタイムまたは要約モデルが不足しているため、後から要約を実行できません。";
            IsSetupWizardModalOpen = true;
            return;
        }

        var segments = _transcriptSegmentRepository.ReadSegments(SelectedJob.JobId);
        if (segments.Count == 0)
        {
            LatestLog = $"素起こし結果がないため、後から{GetPostProcessModeDisplayName(mode)}を実行できません。";
            RefreshPostProcessCommandStates();
            return;
        }

        if (!ConfirmOverwriteExistingPostProcessOutputs(mode, SelectedJob.JobId))
        {
            LatestLog = $"{GetPostProcessModeDisplayName(mode)}の後処理実行をキャンセルしました。";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsPostProcessInProgress = true;
        IsRunInProgress = true;

        try
        {
            _jobLogRepository.AddEvent(
                SelectedJob.JobId,
                "postprocess",
                "info",
                $"Started deferred {GetPostProcessModeLogName(mode)} post-processing.");
            RefreshLogs();

            switch (mode)
            {
                case PostProcessMode.ReviewOnly:
                    await _jobRunCoordinator.RunReviewOnlyAsync(SelectedJob, segments, ApplyRunUpdate, cancellation.Token);
                    ReloadSegmentsForSelectedJob(SelectedSegment?.SegmentId);
                    LoadReviewQueue();
                    break;
                case PostProcessMode.SummaryOnly:
                    await _jobRunCoordinator.RunSummaryOnlyAsync(SelectedJob, ApplyRunUpdate, cancellation.Token);
                    LoadSummaryForSelectedJob();
                    break;
                case PostProcessMode.ReviewAndSummary:
                    await _jobRunCoordinator.RunReviewAndSummaryAsync(SelectedJob, segments, ApplyRunUpdate, cancellation.Token);
                    ReloadSegmentsForSelectedJob(SelectedSegment?.SegmentId);
                    LoadReviewQueue();
                    LoadSummaryForSelectedJob();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
        finally
        {
            _runCancellation = null;
            IsRunInProgress = false;
            IsPostProcessInProgress = false;
        }
    }

    private bool HasExistingReviewOutput(string jobId)
    {
        return _correctionDraftRepository.ReadPendingForJob(jobId).Count > 0 ||
            _transcriptSegmentRepository.ReadPreviews(jobId).Any(static segment => !string.IsNullOrWhiteSpace(segment.FinalText));
    }

    private bool HasExistingSummaryOutput(string jobId)
    {
        return _transcriptDerivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Summary) is not null;
    }

    private bool HasExistingReadablePolishingOutput(string jobId)
    {
        return _transcriptDerivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished) is not null;
    }

    private async Task RunReadablePolishingAsync()
    {
        if (SelectedJob is null || IsRunInProgress || IsPostProcessInProgress)
        {
            return;
        }

        if (!ReviewStageAssetsReady)
        {
            LatestLog = "読みやすく整文するためのランタイムまたは整文モデルが不足しています。";
            IsSetupWizardModalOpen = true;
            return;
        }

        var job = SelectedJob;
        var segments = _transcriptSegmentRepository.ReadSegments(job.JobId);
        if (segments.Count == 0)
        {
            LatestLog = "素起こし結果がないため、読みやすく整文を実行できません。";
            RefreshPostProcessCommandStates();
            return;
        }

        if (HasExistingReadablePolishingOutput(job.JobId) &&
            !ConfirmAction(
                "読みやすく整文を再生成",
                "既存の読みやすく整文結果があります。新しい結果を生成して最新として扱います。続行しますか？"))
        {
            LatestLog = "読みやすく整文をキャンセルしました。";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsPostProcessInProgress = true;
        IsRunInProgress = true;
        IsReadablePolishingInProgress = true;
        ReadablePolishedStatus = "読みやすく整文を生成中です。完了すると結果タブに表示されます。";
        var startedAt = DateTimeOffset.Now;
        MarkReadablePolishingStageRunning();

        try
        {
            var catalog = _modelCatalogService.LoadBuiltInCatalog();
            var modelId = ResolveEffectiveReviewModelId(catalog);
            var profile = new LlmProfileResolver(Paths, _installedModelRepository).Resolve(catalog, modelId);
            var taskSettings = new LlmTaskSettingsResolver().Resolve(profile, LlmTaskKind.Polishing);
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "polishing");

            _jobLogRepository.AddEvent(job.JobId, "polishing", "info", LlmExecutionLogFormatter.Format(profile, taskSettings));
            LatestLog = "読みやすく整文を実行しています。";
            RefreshLogs();

            var result = await _transcriptPolishingService.PolishAsync(
                new TranscriptPolishingOptions(
                    job.JobId,
                    profile.LlamaCompletionPath,
                    profile.ModelPath,
                    outputDirectory,
                    profile.ModelId,
                    taskSettings.PromptTemplateId,
                    taskSettings.GenerationProfile,
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
                    ThreadsBatch: profile.ThreadsBatch),
                cancellation.Token);

            if (string.IsNullOrWhiteSpace(result.Content))
            {
                _jobLogRepository.AddEvent(job.JobId, "polishing", "error", $"Readable polishing failed derivative: {result.DerivativeId}");
                LatestLog = "読みやすく整文に失敗しました。";
                MarkReadablePolishingStageFailed(startedAt);
            }
            else
            {
                _jobLogRepository.AddEvent(job.JobId, "polishing", "info", $"Generated readable polished derivative: {result.DerivativeId}");
                LatestLog = "読みやすく整文が完了しました。";
                MarkReadablePolishingStageSucceeded(startedAt);
                LoadReadablePolishedForSelectedJob();
                SelectedTranscriptTabIndex = 3;
            }

            RefreshJobViews();
            RefreshLogs();
            UpdateExportCommandStates();
        }
        catch (OperationCanceledException)
        {
            _jobLogRepository.AddEvent(job.JobId, "polishing", "info", "Readable polishing was cancelled.");
            LatestLog = "読みやすく整文を中止しました。";
            MarkReadablePolishingStageCancelled(startedAt);
            RefreshLogs();
        }
        catch (ReviewWorkerException exception)
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "polishing");
            _jobLogRepository.AddEvent(
                job.JobId,
                "polishing",
                "error",
                JobLogDiagnostics.FormatException(exception.Category.ToString(), exception, outputDirectory));
            LatestLog = $"読みやすく整文に失敗しました ({exception.Category}): {exception.Message}";
            MarkReadablePolishingStageFailed(startedAt);
            RefreshLogs();
        }
        catch (Exception exception)
        {
            var outputDirectory = Path.Combine(Paths.Jobs, job.JobId, "polishing");
            _jobLogRepository.AddEvent(
                job.JobId,
                "polishing",
                "error",
                JobLogDiagnostics.FormatException(ReviewFailureCategory.Unknown.ToString(), exception, outputDirectory));
            LatestLog = $"読みやすく整文に失敗しました: {exception.Message}";
            MarkReadablePolishingStageFailed(startedAt);
            RefreshLogs();
        }
        finally
        {
            _runCancellation = null;
            IsReadablePolishingInProgress = false;
            IsRunInProgress = false;
            IsPostProcessInProgress = false;
        }
    }

    private void MarkReadablePolishingStageRunning()
    {
        var stage = GetStageStatus(JobRunStage.Review);
        stage.IsRunning = true;
        stage.Status = "進行中";
        stage.ProgressPercent = 10;
        stage.DurationText = "00:00:00";
    }

    private void MarkReadablePolishingStageSucceeded(DateTimeOffset startedAt)
    {
        MarkReadablePolishingStageFinished(startedAt, "完了");
    }

    private void MarkReadablePolishingStageFailed(DateTimeOffset startedAt)
    {
        MarkReadablePolishingStageFinished(startedAt, "失敗");
    }

    private void MarkReadablePolishingStageCancelled(DateTimeOffset startedAt)
    {
        MarkReadablePolishingStageFinished(startedAt, "中止");
    }

    private void MarkReadablePolishingStageFinished(DateTimeOffset startedAt, string status)
    {
        var stage = GetStageStatus(JobRunStage.Review);
        stage.IsRunning = false;
        stage.Status = status;
        stage.ProgressPercent = 100;
        stage.DurationText = FormatStageDuration(DateTimeOffset.Now - startedAt);
    }

    private bool ConfirmOverwriteExistingPostProcessOutputs(PostProcessMode mode, string jobId)
    {
        var hasReviewOutput = (mode == PostProcessMode.ReviewOnly || mode == PostProcessMode.ReviewAndSummary) &&
            HasExistingReviewOutput(jobId);
        var hasSummaryOutput = (mode == PostProcessMode.SummaryOnly || mode == PostProcessMode.ReviewAndSummary) &&
            HasExistingSummaryOutput(jobId);
        if (!hasReviewOutput && !hasSummaryOutput)
        {
            return true;
        }

        var existingOutputText = (hasReviewOutput, hasSummaryOutput) switch
        {
            (true, true) => "既存の整文候補または適用済み整文と、既存の要約",
            (true, false) => "既存の整文候補または適用済み整文",
            (false, true) => "既存の要約",
            _ => string.Empty
        };

        return ConfirmAction(
            "後処理の上書き確認",
            $"{existingOutputText}があります。{GetPostProcessModeDisplayName(mode)}を後から実行すると、新しい結果で上書き・追加生成されます。続行しますか？");
    }

    private void RefreshPostProcessCommandStates()
    {
        OnPropertyChanged(nameof(CanRunPostReview));
        OnPropertyChanged(nameof(CanRunPostSummary));
        OnPropertyChanged(nameof(CanRunReadablePolishing));
        OnPropertyChanged(nameof(CanRunPostReviewAndSummary));
        OnPropertyChanged(nameof(CanRunSelectedJob));
        if (RunPostReviewCommand is RelayCommand reviewCommand)
        {
            reviewCommand.RaiseCanExecuteChanged();
        }

        if (RunPostSummaryCommand is RelayCommand summaryCommand)
        {
            summaryCommand.RaiseCanExecuteChanged();
        }

        if (RunReadablePolishingCommand is RelayCommand polishingCommand)
        {
            polishingCommand.RaiseCanExecuteChanged();
        }

        if (RunPostReviewAndSummaryCommand is RelayCommand bothCommand)
        {
            bothCommand.RaiseCanExecuteChanged();
        }

        if (RunSelectedJobCommand is RelayCommand runCommand)
        {
            runCommand.RaiseCanExecuteChanged();
        }
    }

    private static string GetPostProcessModeDisplayName(PostProcessMode mode)
    {
        return mode switch
        {
            PostProcessMode.ReviewOnly => "整文",
            PostProcessMode.SummaryOnly => "要約",
            PostProcessMode.ReviewAndSummary => "整文・要約",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }

    private static string GetPostProcessModeLogName(PostProcessMode mode)
    {
        return mode switch
        {
            PostProcessMode.ReviewOnly => "review",
            PostProcessMode.SummaryOnly => "summary",
            PostProcessMode.ReviewAndSummary => "review_and_summary",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null)
        };
    }
}
