using KoeNote.App.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void LoadReviewQueue()
    {
        ReviewQueue.Clear();

        if (SelectedJob is not null)
        {
            foreach (var draft in _correctionDraftRepository.ReadPendingForJob(SelectedJob.JobId))
            {
                ReviewQueue.Add(draft);
            }
        }

        SelectedCorrectionDraft = ReviewQueue.FirstOrDefault();
        UpdateReviewCommandStates();
    }

    private void SelectFirstDraftForSegment(string segmentId)
    {
        if (string.IsNullOrWhiteSpace(segmentId))
        {
            return;
        }

        var draft = ReviewQueue.FirstOrDefault(item => item.SegmentId == segmentId);
        if (draft is not null)
        {
            SelectedCorrectionDraft = draft;
        }
    }

    private void ApplySelectedDraftToReviewPane()
    {
        if (SelectedCorrectionDraft is null)
        {
            ClearReviewPreview();
        }
        else
        {
            ReviewIssueType = SelectedCorrectionDraft.IssueType;
            OriginalText = SelectedCorrectionDraft.OriginalText;
            SuggestedText = SelectedCorrectionDraft.SuggestedText;
            ReviewReason = SelectedCorrectionDraft.Reason;
            Confidence = SelectedCorrectionDraft.Confidence;
            SelectSegmentForDraft(SelectedCorrectionDraft);
        }

        OnPropertyChanged(nameof(SelectedCorrectionDraftId));
        OnPropertyChanged(nameof(DraftPositionText));
        UpdateReviewCommandStates();
    }

    private void SelectSegmentForDraft(CorrectionDraft draft)
    {
        var segment = Segments.FirstOrDefault(item => item.SegmentId == draft.SegmentId);
        if (segment is not null && !EqualityComparer<TranscriptSegmentPreview>.Default.Equals(SelectedSegment, segment))
        {
            _selectedSegment = segment;
            OnPropertyChanged(nameof(SelectedSegment));
        }
    }

    private async Task AcceptSelectedDraftAsync()
    {
        await ApplyReviewOperationAsync(static (service, draftId, _) => service.AcceptDraft(draftId));
    }

    private async Task RejectSelectedDraftAsync()
    {
        await ApplyReviewOperationAsync(static (service, draftId, _) => service.RejectDraft(draftId));
    }

    private async Task ApplyManualEditAsync()
    {
        await ApplyReviewOperationAsync(static (service, draftId, finalText) => service.ApplyManualEdit(
            draftId,
            finalText,
            "手修正"));
    }

    private Task SelectPreviousDraftAsync()
    {
        var index = SelectedCorrectionDraft is null ? -1 : ReviewQueue.IndexOf(SelectedCorrectionDraft);
        if (index > 0)
        {
            SelectedCorrectionDraft = ReviewQueue[index - 1];
        }

        return Task.CompletedTask;
    }

    private Task SelectNextDraftAsync()
    {
        var index = SelectedCorrectionDraft is null ? -1 : ReviewQueue.IndexOf(SelectedCorrectionDraft);
        if (index >= 0 && index + 1 < ReviewQueue.Count)
        {
            SelectedCorrectionDraft = ReviewQueue[index + 1];
        }

        return Task.CompletedTask;
    }

    private async Task ApplyReviewOperationAsync(Func<ReviewOperationService, string, string, ReviewOperationResult> operation)
    {
        if (SelectedCorrectionDraft is null || IsReviewOperationInProgress)
        {
            return;
        }

        var currentDraftId = SelectedCorrectionDraft.DraftId;
        var finalText = SuggestedText;
        try
        {
            IsReviewOperationInProgress = true;
            var result = await Task.Run(() => operation(_reviewOperationService, currentDraftId, finalText));
            ApplyReviewOperationResult(currentDraftId, result);
        }
        catch (Exception exception) when (exception is InvalidOperationException or KeyNotFoundException or ArgumentException)
        {
            LatestLog = $"レビュー操作に失敗しました: {exception.Message}";
            LoadReviewQueue();
        }
        finally
        {
            IsReviewOperationInProgress = false;
        }
    }

    private void ApplyReviewOperationResult(string decidedDraftId, ReviewOperationResult result)
    {
        var decidedIndex = ReviewQueue.ToList().FindIndex(item => item.DraftId == decidedDraftId);
        var decidedDraft = ReviewQueue.FirstOrDefault(item => item.DraftId == decidedDraftId);
        if (decidedDraft is not null)
        {
            ReviewQueue.Remove(decidedDraft);
            UpdateCorrectionMemory(decidedDraft, result);
        }

        UpdateSegmentAfterDecision(result);
        if (SelectedJob is not null && SelectedJob.JobId == result.JobId)
        {
            SelectedJob.UnreviewedDrafts = result.PendingDraftCount;
        }

        RefreshJobViews();
        LatestLog = $"Review decision saved: {result.Action} ({result.PendingDraftCount} pending)";

        if (ReviewQueue.Count == 0)
        {
            SelectedCorrectionDraft = null;
            return;
        }

        var nextIndex = Math.Clamp(decidedIndex, 0, ReviewQueue.Count - 1);
        SelectedCorrectionDraft = ReviewQueue[nextIndex];
    }

    private void UpdateCorrectionMemory(CorrectionDraft decidedDraft, ReviewOperationResult result)
    {
        if (string.Equals(result.Action, "rejected", StringComparison.Ordinal))
        {
            _correctionMemoryService.RecordDraftDecision(decidedDraft, "rejected");
            return;
        }

        if (string.Equals(decidedDraft.Source, "memory", StringComparison.OrdinalIgnoreCase))
        {
            _correctionMemoryService.RecordDraftDecision(decidedDraft, result.Action);
            if (RememberCorrection &&
                string.Equals(result.Action, "manual_edit", StringComparison.Ordinal) &&
                result.FinalText is not null)
            {
                _correctionMemoryService.RememberCorrection(
                    decidedDraft with { Source = "llm", SourceRefId = null },
                    result.FinalText);
            }

            return;
        }

        if (RememberCorrection && result.FinalText is not null)
        {
            _correctionMemoryService.RememberCorrection(decidedDraft, result.FinalText);
        }
    }

    private void UpdateSegmentAfterDecision(ReviewOperationResult result)
    {
        for (var i = 0; i < Segments.Count; i++)
        {
            if (Segments[i].SegmentId != result.SegmentId)
            {
                continue;
            }

            var hasPendingDraft = ReviewQueue.Any(item => item.SegmentId == result.SegmentId);
            var nextText = result.FinalText ?? Segments[i].Text;
            Segments[i] = Segments[i] with
            {
                Text = nextText,
                ReviewState = hasPendingDraft ? "推敲候補あり" : "レビュー済み"
            };
            break;
        }

        FilteredSegments.Refresh();
    }

    private bool CanOperateOnSelectedDraft()
    {
        return SelectedCorrectionDraft is not null && !IsRunInProgress && !IsReviewOperationInProgress;
    }

    private bool CanSelectPreviousDraft()
    {
        return SelectedCorrectionDraft is not null && !IsReviewOperationInProgress && ReviewQueue.IndexOf(SelectedCorrectionDraft) > 0;
    }

    private bool CanSelectNextDraft()
    {
        if (SelectedCorrectionDraft is null || IsReviewOperationInProgress)
        {
            return false;
        }

        var index = ReviewQueue.IndexOf(SelectedCorrectionDraft);
        return index >= 0 && index + 1 < ReviewQueue.Count;
    }

    private void RefreshDiffTokens()
    {
        DiffTokens.Clear();
        foreach (var token in _textDiffService.BuildInlineDiff(OriginalText, SuggestedText))
        {
            DiffTokens.Add(token);
        }
    }

    private void UpdateReviewCommandStates()
    {
        if (AcceptDraftCommand is RelayCommand acceptCommand)
        {
            acceptCommand.RaiseCanExecuteChanged();
        }

        if (RejectDraftCommand is RelayCommand rejectCommand)
        {
            rejectCommand.RaiseCanExecuteChanged();
        }

        if (ApplyManualEditCommand is RelayCommand manualCommand)
        {
            manualCommand.RaiseCanExecuteChanged();
        }

        if (SelectPreviousDraftCommand is RelayCommand previousCommand)
        {
            previousCommand.RaiseCanExecuteChanged();
        }

        if (SelectNextDraftCommand is RelayCommand nextCommand)
        {
            nextCommand.RaiseCanExecuteChanged();
        }
    }
}
