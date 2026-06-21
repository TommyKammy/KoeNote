using KoeNote.App.Models;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Audio;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public Func<string, string, bool> ConfirmAction { get; set; } = ShowConfirmation;

    public Func<ConfirmationDialogRequest, bool> ConfirmDialog { get; set; } = ShowConfirmation;

    public Func<SpeakerNameConfirmationRequest, SpeakerNameConfirmationResult?> ConfirmSpeakerNamesDialog { get; set; } =
        ShowSpeakerNameConfirmation;

    public Func<ReviewCandidateConfirmationRequest, ReviewCandidateConfirmationResult?> ConfirmReviewCandidatesDialog { get; set; } =
        ShowReviewCandidateConfirmation;

    private Task AddAudioAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "音声ファイルを選択",
            Filter = "Audio files|*.wav;*.mp3;*.m4a;*.flac;*.aac;*.ogg;*.opus|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        RegisterAudioFile(dialog.FileName);
        return Task.CompletedTask;
    }

    public JobSummary RegisterAudioFile(string audioPath)
    {
        var job = _jobRepository.CreateFromAudio(audioPath);
        Jobs.Insert(0, job);
        FilteredJobs.Refresh();
        SelectedJob = job;
        LatestLog = $"Registered audio job: {job.FileName}";
        _jobLogRepository.AddEvent(job.JobId, "created", "info", $"Registered audio file: {job.SourceAudioPath}");
        RefreshLogs();
        OnPropertyChanged(nameof(JobCountSummary));
        OnPropertyChanged(nameof(StandardLayoutJobBadgeText));
        RefreshJobCommandStates();
        return job;
    }

    public void RegisterAudioFiles(IEnumerable<string> audioPaths)
    {
        var registered = 0;
        foreach (var audioPath in audioPaths.Where(IsSupportedAudioFile))
        {
            RegisterAudioFile(audioPath);
            registered++;
        }

        if (registered == 0)
        {
            LatestLog = "音声ファイルをドロップしてください。";
            return;
        }

        LatestLog = $"{registered}件の音声ファイルを登録しました。";
    }

    private Task DeleteJobAsync(JobSummary? job)
    {
        if (job is null || IsRunInProgress)
        {
            return Task.CompletedTask;
        }

        var willMoveToHistory = !IsUnstartedRegisteredJob(job);
        var confirmationMessage = willMoveToHistory
            ? $"「{job.Title}」をクリア履歴へ移動します。後から復元できます。"
            : $"「{job.Title}」は文字起こし未実施のため、クリア履歴に残さず一覧から削除します。";
        if (!ConfirmAction("ジョブのクリア", confirmationMessage))
        {
            return Task.CompletedTask;
        }

        var movedToHistory = _jobRepository.DeleteJob(job.JobId);
        Jobs.Remove(job);
        if (movedToHistory)
        {
            job.IsDeleted = true;
            job.DeletedAt = DateTimeOffset.Now;
            job.DeleteReason = "manual";
            job.StorageBytes = _jobRepository.GetJobStorageBytes(job.JobId);
            DeletedJobs.Insert(0, job);
        }

        if (SelectedJob?.JobId == job.JobId)
        {
            SelectedJob = Jobs.FirstOrDefault();
        }

        if (Jobs.Count == 0)
        {
            Segments.Clear();
            ReviewQueue.Clear();
            Logs.Clear();
            ClearReviewPreview();
        }

        FilteredJobs.Refresh();
        FilteredDeletedJobs.Refresh();
        OnPropertyChanged(nameof(JobCountSummary));
        OnPropertyChanged(nameof(StandardLayoutJobBadgeText));
        OnPropertyChanged(nameof(DeletedJobCountSummary));
        LatestLog = $"Deleted job: {job.FileName}";
        RefreshJobCommandStates();
        return Task.CompletedTask;
    }

    private Task ClearAllJobsAsync()
    {
        if (IsRunInProgress)
        {
            return Task.CompletedTask;
        }

        var unstartedCount = Jobs.Count(IsUnstartedRegisteredJob);
        var restorableCount = Jobs.Count - unstartedCount;
        var confirmationMessage = (unstartedCount, restorableCount) switch
        {
            (0, _) => $"{Jobs.Count} 件のジョブをクリア履歴へ移動します。後から復元できます。",
            (_, 0) => $"{Jobs.Count} 件のジョブは文字起こし未実施のため、クリア履歴に残さず一覧から削除します。",
            _ => $"{Jobs.Count} 件のジョブをクリアします。文字起こし未実施の {unstartedCount} 件は履歴に残さず削除し、実行済みの {restorableCount} 件はクリア履歴へ移動します。"
        };
        if (!ConfirmAction("ジョブ一覧のクリア", confirmationMessage))
        {
            return Task.CompletedTask;
        }

        var movedToHistoryIds = _jobRepository.DeleteAllJobs();
        var deletedAt = DateTimeOffset.Now;
        foreach (var job in Jobs.ToArray())
        {
            if (!movedToHistoryIds.Contains(job.JobId))
            {
                continue;
            }

            job.IsDeleted = true;
            job.DeletedAt = deletedAt;
            job.DeleteReason = "clear_all";
            job.StorageBytes = _jobRepository.GetJobStorageBytes(job.JobId);
            DeletedJobs.Insert(0, job);
        }

        Jobs.Clear();
        Segments.Clear();
        ReviewQueue.Clear();
        Logs.Clear();
        SelectedJob = null;
        ClearReviewPreview();
        FilteredJobs.Refresh();
        FilteredDeletedJobs.Refresh();
        OnPropertyChanged(nameof(JobCountSummary));
        OnPropertyChanged(nameof(StandardLayoutJobBadgeText));
        OnPropertyChanged(nameof(DeletedJobCountSummary));
        LatestLog = "Cleared all jobs.";
        RefreshJobCommandStates();
        return Task.CompletedTask;
    }

    private Task RestoreJobAsync(JobSummary? job)
    {
        if (job is null || IsRunInProgress)
        {
            return Task.CompletedTask;
        }

        _jobRepository.RestoreJob(job.JobId);
        DeletedJobs.Remove(job);
        job.IsDeleted = false;
        job.DeletedAt = null;
        job.DeleteReason = string.Empty;
        Jobs.Insert(0, job);
        SelectedJob = job;
        FilteredJobs.Refresh();
        FilteredDeletedJobs.Refresh();
        OnPropertyChanged(nameof(JobCountSummary));
        OnPropertyChanged(nameof(StandardLayoutJobBadgeText));
        OnPropertyChanged(nameof(DeletedJobCountSummary));
        LatestLog = $"Restored job: {job.FileName}";
        RefreshJobCommandStates();
        return Task.CompletedTask;
    }

    private Task PermanentlyDeleteJobAsync(JobSummary? job)
    {
        if (job is null || IsRunInProgress)
        {
            return Task.CompletedTask;
        }

        if (!ConfirmAction(
            "ジョブの完全削除",
            $"「{job.Title}」を完全削除します。DB履歴とジョブフォルダ内の関連ファイルは復元できません。"))
        {
            return Task.CompletedTask;
        }

        _jobRepository.PermanentlyDeleteJob(job.JobId);
        DeletedJobs.Remove(job);
        FilteredDeletedJobs.Refresh();
        OnPropertyChanged(nameof(DeletedJobCountSummary));
        LatestLog = $"Permanently deleted job: {job.FileName}";
        RefreshJobCommandStates();
        return Task.CompletedTask;
    }

    private Task PermanentlyDeleteAllDeletedJobsAsync()
    {
        if (IsRunInProgress || DeletedJobs.Count == 0)
        {
            return Task.CompletedTask;
        }

        if (!ConfirmAction(
            "クリア履歴の完全削除",
            $"{DeletedJobs.Count} 件のクリア履歴を完全削除します。DB履歴とジョブフォルダ内の関連ファイルは復元できません。"))
        {
            return Task.CompletedTask;
        }

        _jobRepository.PermanentlyDeleteAllDeletedJobs();
        DeletedJobs.Clear();
        FilteredDeletedJobs.Refresh();
        OnPropertyChanged(nameof(DeletedJobCountSummary));
        LatestLog = "Permanently deleted cleared job history.";
        RefreshJobCommandStates();
        return Task.CompletedTask;
    }

    private void LoadJobs()
    {
        foreach (var job in _jobRepository.LoadRecent())
        {
            Jobs.Add(job);
        }

        foreach (var job in _jobRepository.LoadDeleted())
        {
            DeletedJobs.Add(job);
        }

        SelectedJob = Jobs.FirstOrDefault();
        OnPropertyChanged(nameof(JobCountSummary));
        OnPropertyChanged(nameof(StandardLayoutJobBadgeText));
        OnPropertyChanged(nameof(DeletedJobCountSummary));
        RefreshJobCommandStates();
    }

    private void RefreshLogs()
    {
        Logs.Clear();
        foreach (var entry in _jobLogRepository.ReadLatest(SelectedJob?.JobId))
        {
            Logs.Add(entry);
        }
    }

    private void RefreshJobViews()
    {
        FilteredJobs.Refresh();
        OnPropertyChanged(nameof(SelectedJobNormalizedAudioPath));
        OnPropertyChanged(nameof(SelectedJobPlaybackPath));
        OnPropertyChanged(nameof(SelectedJobUpdatedAt));
        OnPropertyChanged(nameof(SelectedJobUnreviewedDrafts));
        OnPropertyChanged(nameof(DetailInspectorTargetText));
        NotifyStandardLayoutShellChanged();
        RefreshPlaybackWaveform();
        RefreshJobCommandStates();
        UpdatePlaybackCommandStates();
    }

    private void ReloadSelectedJobState()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var latest = _jobRepository.LoadRecent()
            .FirstOrDefault(job => string.Equals(job.JobId, SelectedJob.JobId, StringComparison.Ordinal));
        if (latest is null)
        {
            return;
        }

        SelectedJob.Status = latest.Status;
        SelectedJob.ProgressPercent = latest.ProgressPercent;
        SelectedJob.UnreviewedDrafts = latest.UnreviewedDrafts;
        SelectedJob.UpdatedAt = latest.UpdatedAt;
        SelectedJob.NormalizedAudioPath = latest.NormalizedAudioPath;
    }

    private void RefreshJobCommandStates()
    {
        if (ClearAllJobsCommand is RelayCommand clearAllCommand)
        {
            clearAllCommand.RaiseCanExecuteChanged();
        }

        if (DeleteJobCommand is RelayCommand<JobSummary> deleteCommand)
        {
            deleteCommand.RaiseCanExecuteChanged();
        }

        if (RestoreJobCommand is RelayCommand<JobSummary> restoreCommand)
        {
            restoreCommand.RaiseCanExecuteChanged();
        }

        if (PermanentlyDeleteJobCommand is RelayCommand<JobSummary> purgeCommand)
        {
            purgeCommand.RaiseCanExecuteChanged();
        }

        if (PermanentlyDeleteAllDeletedJobsCommand is RelayCommand purgeAllCommand)
        {
            purgeAllCommand.RaiseCanExecuteChanged();
        }
    }

    private Task PlayPauseAudioAsync()
    {
        var audioPath = ResolveSelectedJobPlaybackPath();
        if (audioPath is null)
        {
            LatestLog = "No playable audio is available for the selected job.";
            return Task.CompletedTask;
        }

        IsAudioPlaying = _audioPlaybackService.Toggle(audioPath);
        if (IsAudioPlaying)
        {
            RequestPlaybackAutoScrollToCurrentPosition(force: true);
        }

        LatestLog = IsAudioPlaying
            ? $"Playing audio: {audioPath}"
            : "Audio playback paused.";
        return Task.CompletedTask;
    }

    private bool CanPlaySelectedJobAudio()
    {
        return ResolveSelectedJobPlaybackPath() is not null;
    }

    private Task SkipToPreviousSegmentAsync()
    {
        var orderedSegments = GetPlaybackOrderedSegments();
        if (orderedSegments.Length == 0)
        {
            return Task.CompletedTask;
        }

        var positionSeconds = PlaybackPositionSeconds;
        var currentIndex = Array.FindLastIndex(orderedSegments, segment => segment.StartSeconds <= positionSeconds + 0.05);
        if (currentIndex < 0)
        {
            SkipPlaybackToSegment(orderedSegments[0]);
            return Task.CompletedTask;
        }

        var current = orderedSegments[currentIndex];
        var targetIndex = positionSeconds - current.StartSeconds > 1.0
            ? currentIndex
            : Math.Max(0, currentIndex - 1);
        SkipPlaybackToSegment(orderedSegments[targetIndex]);
        return Task.CompletedTask;
    }

    private Task SkipToNextSegmentAsync()
    {
        var orderedSegments = GetPlaybackOrderedSegments();
        var next = orderedSegments.FirstOrDefault(segment => segment.StartSeconds > PlaybackPositionSeconds + 0.05);
        if (next is not null)
        {
            SkipPlaybackToSegment(next);
        }

        return Task.CompletedTask;
    }

    private bool CanSkipPlaybackSegment()
    {
        return Segments.Count > 0;
    }

    private TranscriptSegmentPreview[] GetPlaybackOrderedSegments()
    {
        return JobPlaybackPresenter.OrderSegmentsForPlayback(Segments);
    }

    private string? ResolveSelectedJobPlaybackPath()
    {
        return JobPlaybackPresenter.ResolvePlaybackPath(SelectedJob);
    }

    private static string? ResolveJobPlaybackPath(JobSummary job)
    {
        return JobPlaybackPresenter.ResolvePlaybackPath(job);
    }

    private void SeekPlaybackToSelectedSegment(TranscriptSegmentPreview segment)
    {
        var audioPath = ResolveSelectedJobPlaybackPath();
        if (audioPath is not null)
        {
            _audioPlaybackService.Open(audioPath);
            IsAudioPlaying = _audioPlaybackService.IsPlaying;
        }

        PlaybackPositionSeconds = segment.StartSeconds;
    }

    private void SkipPlaybackToSegment(TranscriptSegmentPreview segment)
    {
        _isSelectingSegmentForPlayback = true;
        try
        {
            SelectedSegment = segment;
        }
        finally
        {
            _isSelectingSegmentForPlayback = false;
        }

        SeekPlaybackToSelectedSegment(segment);
        TranscriptAutoScrollRequestId++;
    }

    private void SelectSegmentForPlaybackPosition(double positionSeconds)
    {
        RequestPlaybackAutoScrollToPosition(positionSeconds, force: false);
    }

    private void RequestPlaybackAutoScrollToCurrentPosition(bool force)
    {
        RequestPlaybackAutoScrollToPosition(PlaybackPositionSeconds, force);
    }

    private void RequestPlaybackAutoScrollToPosition(double positionSeconds, bool force)
    {
        if (!IsTranscriptAutoScrollEnabled || Segments.Count == 0 || HasPendingSelectedSegmentEdit())
        {
            return;
        }

        var visibleSegments = Segments
            .Where(segment => FilteredSegments.Contains(segment))
            .ToArray();
        var segment = JobPlaybackPresenter.FindSegmentForPlaybackPosition(visibleSegments, positionSeconds);

        if (segment is null)
        {
            return;
        }

        var isSameSegment = EqualityComparer<TranscriptSegmentPreview>.Default.Equals(SelectedSegment, segment);
        if (!isSameSegment)
        {
            _isSelectingSegmentForPlayback = true;
            try
            {
                SelectedSegment = segment;
            }
            finally
            {
                _isSelectingSegmentForPlayback = false;
            }
        }

        if (force || !isSameSegment)
        {
            TranscriptAutoScrollRequestId++;
        }
    }

    private bool HasPendingSelectedSegmentEdit()
    {
        return SelectedSegment is not null &&
            (IsSegmentInlineEditActive ||
            (!string.Equals(SelectedSegmentEditText, GetSegmentEditableText(SelectedSegment), StringComparison.Ordinal) ||
                !string.Equals(SelectedSpeakerAlias, SelectedSegment.Speaker, StringComparison.Ordinal)));
    }

    private void StopAudioPlayback()
    {
        if (!IsAudioPlaying && _audioPlaybackService.CurrentPath is null)
        {
            RefreshPlaybackState();
            return;
        }

        _audioPlaybackService.Stop();
        IsAudioPlaying = false;
        RefreshPlaybackState();
    }

    private void UpdatePlaybackCommandStates()
    {
        if (PlayPauseAudioCommand is RelayCommand playPauseCommand)
        {
            playPauseCommand.RaiseCanExecuteChanged();
        }

        if (SkipToPreviousSegmentCommand is RelayCommand previousCommand)
        {
            previousCommand.RaiseCanExecuteChanged();
        }

        if (SkipToNextSegmentCommand is RelayCommand nextCommand)
        {
            nextCommand.RaiseCanExecuteChanged();
        }
    }

    private void RefreshPlaybackWaveform()
    {
        PlaybackWaveformSamples.Clear();
        var audioPath = ResolveSelectedJobPlaybackPath();
        if (audioPath is null)
        {
            return;
        }

        foreach (var peak in AudioWaveformReader.ReadPeaks(audioPath))
        {
            PlaybackWaveformSamples.Add(peak);
        }
    }

    private void RefreshPlaybackState()
    {
        PlaybackDurationSeconds = JobPlaybackPresenter.ResolveDurationSeconds(_audioPlaybackService.Duration, Segments);

        _isRefreshingPlaybackPosition = true;
        try
        {
            PlaybackPositionSeconds = _audioPlaybackService.Position.TotalSeconds;
        }
        finally
        {
            _isRefreshingPlaybackPosition = false;
        }
    }

    private bool FilterJob(object item)
    {
        if (item is not JobSummary job)
        {
            return true;
        }

        return JobPlaybackPresenter.MatchesJobSearch(job, JobSearchText);
    }

    private static bool IsUnstartedRegisteredJob(JobSummary job)
    {
        return job.ProgressPercent == 0 &&
            string.Equals(job.Status, "登録済み", StringComparison.Ordinal) &&
            job.UnreviewedDrafts == 0 &&
            string.IsNullOrWhiteSpace(job.NormalizedAudioPath);
    }

    private static bool IsSupportedAudioFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".m4a" or ".flac" or ".aac" or ".ogg" or ".opus";
    }

    private static bool ShowConfirmation(string title, string message)
    {
        return ConfirmationDialogService.Default.Confirm(
            Application.Current?.MainWindow,
            ConfirmationDialogRequest.Warning(title, message));
    }

    private static bool ShowConfirmation(ConfirmationDialogRequest request)
    {
        return ConfirmationDialogService.Default.Confirm(
            Application.Current?.MainWindow,
            request);
    }

    private static SpeakerNameConfirmationResult? ShowSpeakerNameConfirmation(SpeakerNameConfirmationRequest request)
    {
        return SpeakerNameConfirmationDialogService.Default.Confirm(
            Application.Current?.MainWindow,
            request);
    }

    private static ReviewCandidateConfirmationResult? ShowReviewCandidateConfirmation(ReviewCandidateConfirmationRequest request)
    {
        return ReviewCandidateConfirmationDialogService.Default.Confirm(
            Application.Current?.MainWindow,
            request);
    }
}
