using KoeNote.App.Models;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
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
            LatestLog = "整文を生成するためのランタイムまたは整文モデルが不足しています。";
            IsSetupWizardModalOpen = true;
            return;
        }

        var job = SelectedJob;
        var segments = _transcriptSegmentRepository.ReadSegments(job.JobId);
        if (segments.Count == 0)
        {
            LatestLog = "素起こし結果がないため、整文を生成できません。";
            RefreshPostProcessCommandStates();
            return;
        }

        if (HasExistingReadablePolishingOutput(job.JobId) &&
            !ConfirmAction(
                "整文を再生成",
                "既存の整文結果があります。新しい結果を生成して最新として扱います。続行しますか？"))
        {
            LatestLog = "整文をキャンセルしました。";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsPostProcessInProgress = true;
        IsRunInProgress = true;

        try
        {
            await RunReadablePolishingForJobAsync(job, cancellation.Token);
        }
        finally
        {
            _runCancellation = null;
            IsRunInProgress = false;
            IsPostProcessInProgress = false;
        }
    }

    private async Task<bool> RunReadablePolishingForJobAsync(JobSummary job, CancellationToken cancellationToken)
    {
        IsReadablePolishingInProgress = true;
        ReadablePolishedStatus = "整文を生成中です。完了すると先頭の整文タブに表示されます。";

        try
        {
            var result = await _readablePolishingStageRunner.RunAsync(job, ApplyRunUpdate, cancellationToken);
            if (result.Succeeded)
            {
                LoadReadablePolishedForSelectedJob();
                SelectedTranscriptTabIndex = ReadableTranscriptTabIndex;
            }
            else
            {
                ReadablePolishedStatus = result.Message;
            }

            UpdateExportCommandStates();
            return result.Succeeded;
        }
        finally
        {
            IsReadablePolishingInProgress = false;
        }
    }

    private ReadablePolishingPromptSettings LoadReadablePolishingPromptSettings(LlmRuntimeProfile profile)
    {
        return ReadablePolishingPromptSettingsResolver.Resolve(profile, _readablePolishingPromptSettingsRepository);
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
            (true, true) => "既存のレビュー候補または適用済みレビュー結果と、既存の要約",
            (true, false) => "既存のレビュー候補または適用済みレビュー結果",
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
            PostProcessMode.ReviewOnly => "レビュー候補",
            PostProcessMode.SummaryOnly => "要約",
            PostProcessMode.ReviewAndSummary => "レビュー候補・要約",
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
