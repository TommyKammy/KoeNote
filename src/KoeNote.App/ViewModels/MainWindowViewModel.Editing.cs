using KoeNote.App.Models;

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

        SelectedSegmentEditText = segment!.Text;
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
            SelectedSegmentEditText = SelectedSegment.Text;
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
            SelectedSegmentEditText = segment.Text;
            IsSegmentInlineEditActive = false;
            return Task.CompletedTask;
        }

        if (_transcriptEditService.UndoLastSegmentEdit(SelectedJob.JobId, segment.SegmentId))
        {
            LatestLog = "選択セグメントの直近編集を戻しました。";
            ReloadSegmentsForSelectedJob(segment.SegmentId);
            LoadSummaryForSelectedJob();
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

        if (string.Equals(SelectedSegmentEditText, segment.Text, StringComparison.Ordinal))
        {
            IsSegmentInlineEditActive = false;
            return true;
        }

        _transcriptEditService.ApplySegmentEdit(
            SelectedJob.JobId,
            segment.SegmentId,
            SelectedSegmentEditText);

        IsSegmentInlineEditActive = false;
        LatestLog = "セグメント本文を手修正しました。元の文字起こしは保持されています。";
        if (reloadSegments)
        {
            ReloadSegmentsForSelectedJob(segment.SegmentId);
        }
        else
        {
            ReplaceEditedSegmentPreview(segment, SelectedSegmentEditText);
            FilteredSegments.Refresh();
        }

        LoadSummaryForSelectedJob();
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
