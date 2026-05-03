namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task SaveSegmentEditAsync()
    {
        if (SelectedJob is null || SelectedSegment is null)
        {
            return Task.CompletedTask;
        }

        _transcriptEditService.ApplySegmentEdit(
            SelectedJob.JobId,
            SelectedSegment.SegmentId,
            SelectedSegmentEditText);

        LatestLog = "セグメント本文を手修正しました。元の文字起こしは保持されています。";
        ReloadSegmentsForSelectedJob(SelectedSegment.SegmentId);
        return Task.CompletedTask;
    }

    private Task SaveSpeakerAliasAsync()
    {
        if (SelectedJob is null || SelectedSegment is null)
        {
            return Task.CompletedTask;
        }

        _transcriptEditService.ApplySpeakerAlias(
            SelectedJob.JobId,
            SelectedSegment.SpeakerId,
            SelectedSpeakerAlias);

        LatestLog = $"話者名を更新しました: {SelectedSegment.SpeakerId} -> {SelectedSpeakerAlias}";
        ReloadSegmentsForSelectedJob(SelectedSegment.SegmentId);
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
            return;
        }

        var previews = _transcriptSegmentRepository.ReadPreviews(SelectedJob.JobId);
        Segments.Clear();
        if (previews.Count == 0)
        {
            SelectedSegment = null;
            RefreshSpeakerFilters();
            FilteredSegments.Refresh();
            UpdateExportCommandStates();
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
    }

    private bool CanEditSelectedSegment()
    {
        return SelectedJob is not null
            && SelectedSegment is not null
            && !IsRunInProgress
            && !string.IsNullOrWhiteSpace(SelectedSegmentEditText);
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

        if (SaveSpeakerAliasCommand is RelayCommand saveSpeakerCommand)
        {
            saveSpeakerCommand.RaiseCanExecuteChanged();
        }
    }
}
