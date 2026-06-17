using KoeNote.App.Models;
using KoeNote.App.Services.Jobs;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task BeginSegmentInlineEditAsync(TranscriptSegmentPreview? segment)
    {
        if (!CanBeginSegmentInlineEdit(segment))
        {
            return Task.CompletedTask;
        }

        SelectedSegment = segment;
        if (!string.Equals(SelectedSegment?.SegmentId, segment!.SegmentId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _segmentInlineEditStartedInRawMode = IsRawTranscriptEditingMode;
        SelectedSegmentEditText = GetSegmentEditableText(segment!, _segmentInlineEditStartedInRawMode);
        IsSpeakerInlineEditActive = false;
        IsSegmentInlineEditActive = true;
        return Task.CompletedTask;
    }

    private Task BeginSpeakerInlineEditAsync(TranscriptSegmentPreview? segment)
    {
        if (!CanBeginSpeakerInlineEdit(segment))
        {
            return Task.CompletedTask;
        }

        SelectedSegment = segment;
        if (!string.Equals(SelectedSegment?.SegmentId, segment!.SegmentId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        SelectedSpeakerAlias = segment!.Speaker;
        IsSegmentInlineEditActive = false;
        IsSpeakerInlineEditActive = true;
        return Task.CompletedTask;
    }

    private Task SaveSegmentEditAsync()
    {
        if (SelectedSegment is null)
        {
            return Task.CompletedTask;
        }

        CommitSegmentInlineEdit(SelectedSegment, reloadSegments: true);
        return Task.CompletedTask;
    }

    private Task SaveSegmentInlineEditAsync()
    {
        return SaveSegmentEditAsync();
    }

    private Task CancelSegmentInlineEditAsync()
    {
        if (SelectedSegment is not null)
        {
            SelectedSegmentEditText = GetSegmentEditableText(SelectedSegment, _segmentInlineEditStartedInRawMode);
        }

        IsSegmentInlineEditActive = false;
        return Task.CompletedTask;
    }

    private Task SaveSpeakerInlineEditAsync()
    {
        if (SelectedSegment is null)
        {
            return Task.CompletedTask;
        }

        CommitSpeakerInlineEdit(SelectedSegment, reloadSegments: true);
        return Task.CompletedTask;
    }

    private Task RevertSegmentEditAsync(TranscriptSegmentPreview? segment)
    {
        if (SelectedJob is null || segment is null)
        {
            return Task.CompletedTask;
        }

        if (IsSegmentInlineEditActive &&
            SelectedSegment is not null &&
            string.Equals(SelectedSegment.SegmentId, segment.SegmentId, StringComparison.Ordinal))
        {
            SelectedSegmentEditText = GetSegmentEditableText(segment, _segmentInlineEditStartedInRawMode);
            IsSegmentInlineEditActive = false;
            return Task.CompletedTask;
        }

        var reverted = IsRawTranscriptEditingMode
            ? _transcriptEditService.UndoLastRawSegmentEdit(SelectedJob.JobId, segment.SegmentId)
            : _transcriptEditService.UndoLastSegmentEdit(SelectedJob.JobId, segment.SegmentId);
        if (reverted)
        {
            LatestLog = "選択セグメントの直近編集を戻しました。";
            LoadReviewQueue();
            ReloadSegmentsForSelectedJob(segment.SegmentId);
            LoadSummaryForSelectedJob();
            LoadReadablePolishedForSelectedJob();
            ReloadSelectedJobState();
            RefreshJobViews();
        }
        else
        {
            LatestLog = "このセグメントに戻せる編集履歴はありません。";
        }

        return Task.CompletedTask;
    }

    private bool CommitSegmentInlineEdit(TranscriptSegmentPreview segment, bool reloadSegments)
    {
        if (SelectedJob is null || string.IsNullOrWhiteSpace(SelectedSegmentEditText))
        {
            return false;
        }

        var useRawTranscript = IsSegmentInlineEditActive
            ? _segmentInlineEditStartedInRawMode
            : IsRawTranscriptEditingMode;

        if (string.Equals(SelectedSegmentEditText, GetSegmentEditableText(segment, useRawTranscript), StringComparison.Ordinal))
        {
            IsSegmentInlineEditActive = false;
            return true;
        }

        if (useRawTranscript)
        {
            _transcriptEditService.ApplyRawSegmentEdit(
                SelectedJob.JobId,
                segment.SegmentId,
                SelectedSegmentEditText);
        }
        else
        {
            _transcriptEditService.ApplySegmentEdit(
                SelectedJob.JobId,
                segment.SegmentId,
                SelectedSegmentEditText);
        }

        IsSegmentInlineEditActive = false;
        LatestLog = useRawTranscript
            ? "素起こし本文を手修正しました。"
            : "セグメント本文を手修正しました。元の文字起こしは保持されています。";
        if (reloadSegments)
        {
            ReloadSegmentsForSelectedJob(segment.SegmentId);
        }
        else
        {
            if (useRawTranscript)
            {
                ReplaceEditedRawSegmentPreview(segment, SelectedSegmentEditText);
            }
            else
            {
                ReplaceEditedSegmentPreview(segment, SelectedSegmentEditText);
            }

            FilteredSegments.Refresh();
        }

        LoadSummaryForSelectedJob();
        LoadReadablePolishedForSelectedJob();
        if (useRawTranscript)
        {
            LoadReviewQueue(updateSelection: reloadSegments);
            _suppressNextSegmentDraftSelection = !reloadSegments;
            if (SelectedJob is not null)
            {
                SelectedJob.UnreviewedDrafts = ReviewQueue.Count;
                if (ReviewQueue.Count == 0)
                {
                    SelectedJob.Status = "完成文書作成待ち";
                    SelectedJob.ProgressPercent = JobRunProgressPlan.ReviewSucceeded;
                    MarkManualReviewStageCompleted();
                }
            }

            RefreshJobViews();
        }

        return true;
    }

    private bool CommitSpeakerInlineEdit(TranscriptSegmentPreview segment, bool reloadSegments)
    {
        if (SelectedJob is null ||
            string.IsNullOrWhiteSpace(segment.SpeakerId) ||
            string.IsNullOrWhiteSpace(SelectedSpeakerAlias))
        {
            return false;
        }

        if (string.Equals(SelectedSpeakerAlias, segment.Speaker, StringComparison.Ordinal))
        {
            IsSpeakerInlineEditActive = false;
            return true;
        }

        _transcriptEditService.ApplySpeakerAlias(
            SelectedJob.JobId,
            segment.SpeakerId,
            SelectedSpeakerAlias);

        IsSpeakerInlineEditActive = false;
        LatestLog = $"話者名を更新しました: {segment.SpeakerId} -> {SelectedSpeakerAlias}";
        if (reloadSegments)
        {
            ReloadSegmentsForSelectedJob(segment.SegmentId);
        }
        else
        {
            ReplaceEditedSpeakerPreview(segment, SelectedSpeakerAlias);
            RefreshSpeakerFilters();
            FilteredSegments.Refresh();
        }

        LoadSummaryForSelectedJob();
        LoadReadablePolishedForSelectedJob();
        return true;
    }

    private void ReplaceEditedSegmentPreview(TranscriptSegmentPreview segment, string text)
    {
        var index = Segments.IndexOf(segment);
        if (index >= 0)
        {
            Segments[index] = segment with { Text = text, FinalText = text };
        }
    }

    private void ReplaceEditedRawSegmentPreview(TranscriptSegmentPreview segment, string rawText)
    {
        var index = Segments.IndexOf(segment);
        if (index >= 0)
        {
            Segments[index] = segment with
            {
                Text = rawText,
                RawText = rawText,
                NormalizedText = null,
                FinalText = null,
                ReviewState = "手修正済み"
            };
        }
    }

    private string GetSegmentEditableText(TranscriptSegmentPreview segment)
    {
        return GetSegmentEditableText(segment, IsRawTranscriptEditingMode);
    }

    private static string GetSegmentEditableText(TranscriptSegmentPreview segment, bool useRawTranscript)
    {
        return useRawTranscript ? segment.RawTranscriptText : segment.Text;
    }

    private void RefreshSelectedSegmentEditBuffer()
    {
        if (SelectedSegment is not null && !IsSegmentInlineEditActive)
        {
            SelectedSegmentEditText = GetSegmentEditableText(SelectedSegment);
        }
    }

    private bool IsRawTranscriptEditingMode => EffectiveExportTranscriptTabIndex == ExportRawTranscriptTabIndex;

    private void ReplaceEditedSpeakerPreview(TranscriptSegmentPreview segment, string speaker)
    {
        for (var i = 0; i < Segments.Count; i++)
        {
            if (string.Equals(Segments[i].SpeakerId, segment.SpeakerId, StringComparison.Ordinal))
            {
                Segments[i] = Segments[i] with { Speaker = speaker };
            }
        }
    }

    private Task SaveSpeakerAliasAsync()
    {
        if (SelectedJob is null || SelectedSegment is null)
        {
            return Task.CompletedTask;
        }

        CommitSpeakerInlineEdit(SelectedSegment, reloadSegments: true);
        return Task.CompletedTask;
    }

    private Task UndoLastOperationAsync()
    {
        if (SelectedJob is null)
        {
            LatestLog = "取り消せる操作はありません。";
            return Task.CompletedTask;
        }

        var selectedSegmentId = SelectedSegment?.SegmentId;
        if (_transcriptEditService.UndoLast(SelectedJob.JobId))
        {
            LatestLog = "直前の操作を取り消しました。";
            LoadReviewQueue();
            ReloadSegmentsForSelectedJob(selectedSegmentId);
            LoadSummaryForSelectedJob();
            LoadReadablePolishedForSelectedJob();
            ReloadSelectedJobState();
            RefreshJobViews();
        }
        else
        {
            LatestLog = "取り消せる操作はありません。";
        }

        return Task.CompletedTask;
    }

    private void ReloadSegmentsForSelectedJob(string? preferredSegmentId = null)
    {
        if (SelectedJob is null)
        {
            Segments.Clear();
            SelectedSegment = null;
            OnPropertyChanged(nameof(StandardLayoutMeta));
            OnPropertyChanged(nameof(DetailInspectorSegmentText));
            RefreshPostProcessCommandStates();
            return;
        }

        var previews = _transcriptSegmentRepository.ReadPreviews(SelectedJob.JobId);
        _isReloadingSegments = true;
        try
        {
            Segments.Clear();
            if (previews.Count == 0)
            {
                IsSegmentInlineEditActive = false;
                SelectedSegment = null;
                RefreshSpeakerFilters();
                FilteredSegments.Refresh();
                UpdateExportCommandStates();
                UpdatePlaybackCommandStates();
                OnPropertyChanged(nameof(StandardLayoutMeta));
                OnPropertyChanged(nameof(DetailInspectorSegmentText));
                RefreshPostProcessCommandStates();
                return;
            }

            foreach (var preview in previews)
            {
                Segments.Add(preview);
            }

            RefreshSpeakerFilters();
            FilteredSegments.Refresh();
            SelectedSegment = Segments.FirstOrDefault(segment => segment.SegmentId == preferredSegmentId)
                ?? Segments.FirstOrDefault();
            UpdateExportCommandStates();
            UpdatePlaybackCommandStates();
            RefreshPostProcessCommandStates();
        }
        finally
        {
            _isReloadingSegments = false;
        }

        OnPropertyChanged(nameof(StandardLayoutMeta));
        OnPropertyChanged(nameof(DetailInspectorSegmentText));
    }

    private bool CanEditSelectedSegment()
    {
        return SelectedJob is not null
            && SelectedSegment is not null
            && !IsRunInProgress
            && !string.IsNullOrWhiteSpace(SelectedSegmentEditText);
    }

    private bool CanBeginSegmentInlineEdit(TranscriptSegmentPreview? segment)
    {
        return SelectedJob is not null
            && segment is not null
            && !IsRunInProgress;
    }

    private bool CanBeginSpeakerInlineEdit(TranscriptSegmentPreview? segment)
    {
        return SelectedJob is not null
            && segment is not null
            && !IsRunInProgress
            && !string.IsNullOrWhiteSpace(segment.SpeakerId);
    }

    private bool CanRevertSegmentEdit(TranscriptSegmentPreview? segment)
    {
        return SelectedJob is not null
            && segment is not null
            && !IsRunInProgress;
    }

    private bool CanEditSelectedSpeaker()
    {
        return SelectedJob is not null
            && SelectedSegment is not null
            && !IsRunInProgress
            && !string.IsNullOrWhiteSpace(SelectedSegment.SpeakerId)
            && !string.IsNullOrWhiteSpace(SelectedSpeakerAlias);
    }

    private void UpdateSegmentEditCommandStates()
    {
        if (SaveSegmentEditCommand is RelayCommand saveSegmentCommand)
        {
            saveSegmentCommand.RaiseCanExecuteChanged();
        }

        if (BeginSegmentInlineEditCommand is RelayCommand<TranscriptSegmentPreview> beginInlineCommand)
        {
            beginInlineCommand.RaiseCanExecuteChanged();
        }

        if (SaveSegmentInlineEditCommand is RelayCommand saveInlineCommand)
        {
            saveInlineCommand.RaiseCanExecuteChanged();
        }

        if (CancelSegmentInlineEditCommand is RelayCommand cancelInlineCommand)
        {
            cancelInlineCommand.RaiseCanExecuteChanged();
        }

        if (RevertSegmentEditCommand is RelayCommand<TranscriptSegmentPreview> revertSegmentCommand)
        {
            revertSegmentCommand.RaiseCanExecuteChanged();
        }

        if (BeginSpeakerInlineEditCommand is RelayCommand<TranscriptSegmentPreview> beginSpeakerInlineCommand)
        {
            beginSpeakerInlineCommand.RaiseCanExecuteChanged();
        }

        if (SaveSpeakerInlineEditCommand is RelayCommand saveSpeakerInlineCommand)
        {
            saveSpeakerInlineCommand.RaiseCanExecuteChanged();
        }

        if (SaveSpeakerAliasCommand is RelayCommand saveSpeakerCommand)
        {
            saveSpeakerCommand.RaiseCanExecuteChanged();
        }
    }
}
