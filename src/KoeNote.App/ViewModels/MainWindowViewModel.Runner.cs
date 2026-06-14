using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Transcript;

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

            var asrSettings = new AsrSettings(
                AsrContextText,
                AsrHotwordsText,
                SelectedAsrEngineId,
                enableReviewForRun,
                EnableSummaryStage: false,
                SelectedAsrExecutionProfileId,
                EnableChunkedGpuAsr);
            var runSucceeded = await _jobRunCoordinator.RunAsync(job, asrSettings, ApplyRunUpdate, cancellation.Token);
            var readablePolishingAttempted = false;
            var readablePolishingSucceeded = false;
            if (runSucceeded && enableReviewForRun && !cancellation.IsCancellationRequested)
            {
                readablePolishingAttempted = true;
                if (ConfirmReviewCandidatesBeforeReadablePolishing(job) &&
                    ConfirmSpeakerNamesBeforeReadablePolishing(job))
                {
                    readablePolishingSucceeded = await RunReadablePolishingForJobAsync(job, cancellation.Token);
                }
                else
                {
                    LoadReadablePolishedForSelectedJob();
                }
            }

            LoadSummaryForSelectedJob();
            if (!readablePolishingAttempted || readablePolishingSucceeded)
            {
                LoadReadablePolishedForSelectedJob();
            }
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
        if (update.Stage is { } stage && update.StageState is { } state && update.StageProgressPercent is { } stageProgressPercent)
        {
            if (stage == JobRunStage.Summary)
            {
                IsSummaryStageRunning = state == JobRunStageState.Running;
            }
            else
            {
                var stageStatus = GetStageStatus(stage);
                stageStatus.IsRunning = state == JobRunStageState.Running;
                stageStatus.Status = update.StageStatusText ?? GetStageStatusText(state, update.ErrorCategory);
                stageStatus.ProgressPercent = stageProgressPercent;

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
                    SelectedTranscriptTabIndex = ReadableTranscriptTabIndex;
                    StartPolishedTranscriptTabHighlight();
                }
            }
        }

        if (update.JobProgressPercent is { } jobProgressPercent && SelectedJob is not null)
        {
            SelectedJob.ProgressPercent = jobProgressPercent;
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

    private bool ConfirmReviewCandidatesBeforeReadablePolishing(JobSummary job)
    {
        var drafts = _correctionDraftRepository.ReadPendingForJob(job.JobId);
        if (drafts.Count == 0)
        {
            return true;
        }

        var segments = new TranscriptReadRepository(Paths)
            .ReadForJob(job.JobId)
            .ToDictionary(static segment => segment.SegmentId, StringComparer.Ordinal);
        var candidates = drafts
            .Select(draft =>
            {
                segments.TryGetValue(draft.SegmentId, out var segment);
                return new ReviewCandidateConfirmationItem(
                    draft,
                    segment?.StartSeconds,
                    segment?.EndSeconds,
                    segment?.Speaker,
                    segment?.Text);
            })
            .ToList();

        var result = ConfirmReviewCandidatesDialog(new ReviewCandidateConfirmationRequest(
            job.Title,
            candidates,
            new ReviewCandidateConfirmationOperationAdapter(_reviewOperationService))
        {
            RecordDecision = UpdateCorrectionMemory
        });

        LoadReviewQueue();
        ReloadSegmentsForSelectedJob();
        RefreshJobViews();

        if (result?.Outcome == ReviewCandidateConfirmationOutcome.Continue && result.RemainingPendingCount == 0)
        {
            LatestLog = "整文候補を確認しました。話者名確認へ進みます。";
            return true;
        }

        LatestLog = result?.Outcome == ReviewCandidateConfirmationOutcome.Defer
            ? "整文候補の確認が保留されたため、整文を開始しませんでした。"
            : "整文候補確認のため、整文を開始しませんでした。";
        return false;
    }

    private bool ConfirmSpeakerNamesBeforeReadablePolishing(JobSummary job, bool forceSpeakerConfirmation = false)
    {
        var speakerSummaries = new TranscriptReadRepository(Paths).ReadForJob(job.JobId)
            .Where(static segment => !string.IsNullOrWhiteSpace(segment.SpeakerId))
            .GroupBy(static segment => segment.SpeakerId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(static group =>
            {
                var displayName = group
                    .Select(static segment => segment.Speaker)
                    .FirstOrDefault(static speaker => !string.IsNullOrWhiteSpace(speaker)) ?? group.Key;
                var previews = BuildSpeakerConfirmationPreviews(group);
                return new SpeakerNameConfirmationItem(group.Key, displayName.Trim(), group.Count(), previews);
            })
            .OrderBy(static summary => summary.SpeakerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (speakerSummaries.Count == 0)
        {
            return true;
        }

        if (!forceSpeakerConfirmation &&
            string.Equals(SelectedSpeakerNameConfirmationModeValue, SpeakerNameConfirmationModes.UnassignedOnly, StringComparison.OrdinalIgnoreCase) &&
            !speakerSummaries.Any(IsUnassignedSpeakerName))
        {
            return true;
        }

        var result = ConfirmSpeakerNamesDialog(new SpeakerNameConfirmationRequest(job.Title, speakerSummaries)
        {
            AudioPath = ResolveJobPlaybackPath(job)
        });
        if (result is not null)
        {
            var speakerDisplayChanged = false;
            foreach (var speaker in speakerSummaries)
            {
                if (!result.DisplayNames.TryGetValue(speaker.SpeakerId, out var displayName) ||
                    string.IsNullOrWhiteSpace(displayName) ||
                    string.Equals(displayName.Trim(), speaker.DisplayName, StringComparison.Ordinal))
                {
                    continue;
                }

                var normalizedDisplayName = displayName.Trim();
                _transcriptEditService.ApplySpeakerAlias(job.JobId, speaker.SpeakerId, normalizedDisplayName);
                var matchingSegment = Segments.FirstOrDefault(segment =>
                    string.Equals(segment.SpeakerId, speaker.SpeakerId, StringComparison.OrdinalIgnoreCase));
                if (matchingSegment is not null)
                {
                    ReplaceEditedSpeakerPreview(matchingSegment, normalizedDisplayName);
                    speakerDisplayChanged = true;
                }
            }

            if (speakerDisplayChanged)
            {
                RefreshSpeakerFilters();
                FilteredSegments.Refresh();
            }

            return true;
        }

        LatestLog = "話者名確認のため、整文を開始しませんでした。";
        return false;
    }

    private static bool IsUnassignedSpeakerName(SpeakerNameConfirmationItem speaker)
    {
        return string.Equals(speaker.DisplayName, speaker.SpeakerId, StringComparison.OrdinalIgnoreCase) ||
            speaker.DisplayName.StartsWith("Speaker_", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<SpeakerNameConfirmationPreview> BuildSpeakerConfirmationPreviews(
        IEnumerable<TranscriptReadModel> segments)
    {
        return segments
            .Select(static segment => new
            {
                segment.StartSeconds,
                segment.EndSeconds,
                Text = (segment.FinalText ?? segment.NormalizedText ?? segment.RawText ?? segment.Text).Trim()
            })
            .Where(static sample => !string.IsNullOrWhiteSpace(sample.Text))
            .GroupBy(static sample => sample.Text, StringComparer.Ordinal)
            .Select(static group => group.OrderBy(static sample => sample.StartSeconds).First())
            .OrderByDescending(static sample => sample.Text.Length)
            .ThenBy(static sample => sample.StartSeconds)
            .Take(3)
            .OrderBy(static sample => sample.StartSeconds)
            .Select(static sample => new SpeakerNameConfirmationPreview(
                sample.StartSeconds,
                sample.EndSeconds,
                sample.Text))
            .ToList();
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
