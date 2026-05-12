using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task RunSelectedJobAsync()
    {
        if (SelectedJob is null || IsRunInProgress)
        {
            return;
        }

        var preflightIssues = GetRunPreflightIssues();
        if (preflightIssues.Count > 0)
        {
            LatestLog = "実行前チェックで不足があります: " + string.Join(" / ", preflightIssues);
            IsSetupWizardModalOpen = true;
            OnPropertyChanged(nameof(RunPreflightSummary));
            OnPropertyChanged(nameof(RunPreflightDetail));
            return;
        }

        var job = SelectedJob;
        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        IsSummaryStageRunning = false;
        IsRunInProgress = true;
        SaveAsrSettings();

        try
        {
            var enableReviewForRun = EnableReviewStage && ReviewStageAssetsReady;
            if (EnableReviewStage && !enableReviewForRun)
            {
                LatestLog = "整文ステージの準備が未完了のため、この実行では整文をスキップします。";
            }

            var asrSettings = new AsrSettings(AsrContextText, AsrHotwordsText, SelectedAsrEngineId, enableReviewForRun, EnableSummaryStage: false);
            await _jobRunCoordinator.RunAsync(job, asrSettings, ApplyRunUpdate, cancellation.Token);
            LoadSummaryForSelectedJob();
            LoadReadablePolishedForSelectedJob();
        }
        finally
        {
            _runCancellation = null;
            IsSummaryStageRunning = false;
            IsRunInProgress = false;
        }
    }

    private Task CancelRunAsync()
    {
        _runCancellation?.Cancel();
        LatestLog = "キャンセルを要求しました。";
        return Task.CompletedTask;
    }

    private void ApplyRunUpdate(JobRunUpdate update)
    {
        if (update.Stage is { } stage && update.StageState is { } state && update.ProgressPercent is { } progressPercent)
        {
            if (stage == JobRunStage.Summary)
            {
                IsSummaryStageRunning = state == JobRunStageState.Running;
            }
            else
            {
                var stageStatus = GetStageStatus(stage);
                stageStatus.IsRunning = state == JobRunStageState.Running;
                stageStatus.Status = GetStageStatusText(state, update.ErrorCategory);
                stageStatus.ProgressPercent = progressPercent;

                if (state == JobRunStageState.Running)
                {
                    stageStatus.DurationText = "00:00:00";
                }
                else if (update.Duration is { } duration)
                {
                    stageStatus.DurationText = FormatStageDuration(duration);
                }

                if (stage == JobRunStage.Review && state == JobRunStageState.Succeeded)
                {
                    SelectedTranscriptTabIndex = 1;
                    StartPolishedTranscriptTabHighlight();
                }
            }
        }

        if (update.Segments is not null)
        {
            ReplaceSegments(update.Segments);
        }

        if (update.Drafts is not null)
        {
            ApplyReviewDrafts(update.Drafts);
        }
        else if (update.ClearReviewPreview)
        {
            ReviewQueue.Clear();
            SelectedCorrectionDraft = null;
            ClearReviewPreview();
            MarkManualReviewStageCompleted();
            UpdateReviewCommandStates();
        }

        if (update.RefreshJobViews)
        {
            RefreshJobViews();
        }

        if (update.RefreshLogs)
        {
            RefreshLogs();
        }

        if (update.LatestLog is not null)
        {
            LatestLog = update.LatestLog;
        }

        if (update.Stage == JobRunStage.Asr &&
            update.StageState == JobRunStageState.Failed &&
            string.Equals(update.ErrorCategory, AsrFailureCategory.CudaRuntimeMissing.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            IsSetupWizardModalOpen = true;
            LatestLog = "ASR GPU実行に必要なCUDA runtimeが不足しています。セットアップからASR GPU runtimeを導入してください。";
            RefreshSetupWizard();
        }

        if (update.Stage == JobRunStage.Asr &&
            update.StageState == JobRunStageState.Failed &&
            string.Equals(update.ErrorCategory, AsrFailureCategory.NativeCrash.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            LatestLog = "ASR native runtime crashed. Check the ASR worker log in the diagnostic package, then retry with a lighter ASR model or reinstall the ASR GPU runtime.";
        }
    }

    private StageStatus GetStageStatus(JobRunStage stage)
    {
        return stage switch
        {
            JobRunStage.Preprocess => StageStatuses[0],
            JobRunStage.Asr => StageStatuses.First(item => item.Name == "ASR"),
            JobRunStage.Review => StageStatuses[2],
            _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
        };
    }

    private static string GetStageStatusText(JobRunStageState state, string? errorCategory)
    {
        if (state == JobRunStageState.Skipped)
        {
            return "スキップ";
        }

        return state switch
        {
            JobRunStageState.Running => "進行中",
            JobRunStageState.Succeeded => "完了",
            JobRunStageState.Cancelled => "中止",
            JobRunStageState.Failed when !string.IsNullOrWhiteSpace(errorCategory) => $"失敗: {errorCategory}",
            JobRunStageState.Failed => "失敗",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
    }

    private static string FormatStageDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return duration.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void StartPolishedTranscriptTabHighlight()
    {
        _polishedTranscriptTabHighlightCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _polishedTranscriptTabHighlightCancellation = cancellation;
        IsPolishedTranscriptTabHighlighted = true;
        _ = ClearPolishedTranscriptTabHighlightAfterDelayAsync(cancellation);
    }

    private async Task ClearPolishedTranscriptTabHighlightAfterDelayAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellation.Token);
            if (!cancellation.IsCancellationRequested)
            {
                IsPolishedTranscriptTabHighlighted = false;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_polishedTranscriptTabHighlightCancellation, cancellation))
            {
                _polishedTranscriptTabHighlightCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void ReplaceSegments(IReadOnlyList<TranscriptSegment> segments)
    {
        Segments.Clear();
        foreach (var segment in segments)
        {
            Segments.Add(new TranscriptSegmentPreview(
                FormatTimestamp(segment.StartSeconds),
                FormatTimestamp(segment.EndSeconds),
                segment.SpeakerId ?? "",
                segment.NormalizedText ?? segment.RawText,
                "候補なし",
                segment.SegmentId,
                segment.SpeakerId ?? "",
                segment.RawText,
                segment.NormalizedText,
                null,
                segment.StartSeconds,
                segment.EndSeconds));
        }

        RefreshSpeakerFilters();
        FilteredSegments.Refresh();
    }

    private void ApplyReviewDrafts(IReadOnlyList<CorrectionDraft> drafts)
    {
        ReviewQueue.Clear();
        foreach (var draft in drafts.Where(static draft => draft.Status == "pending"))
        {
            ReviewQueue.Add(draft);
        }

        if (ReviewQueue.Count == 0)
        {
            MarkManualReviewStageCompleted();
            ClearReviewPreview();
            UpdateReviewCommandStates();
            return;
        }

        MarkManualReviewStageWaiting(ReviewQueue.Count);
        SelectedCorrectionDraft = ReviewQueue[0];
        UpdateSegmentReviewStates(drafts);
    }

    private void MarkManualReviewStageWaiting(int pendingCount)
    {
        _ = pendingCount;
    }

    private void MarkManualReviewStageCompleted()
    {
    }

    private void ResetManualReviewStage()
    {
    }
}
