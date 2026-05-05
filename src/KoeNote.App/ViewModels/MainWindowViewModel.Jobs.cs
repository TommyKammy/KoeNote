using KoeNote.App.Models;
using Microsoft.Win32;
using System.Globalization;
using System.IO;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
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

        _jobRepository.DeleteJob(job.JobId);
        Jobs.Remove(job);
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
        OnPropertyChanged(nameof(JobCountSummary));
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

        _jobRepository.DeleteAllJobs();
        Jobs.Clear();
        Segments.Clear();
        ReviewQueue.Clear();
        Logs.Clear();
        SelectedJob = null;
        ClearReviewPreview();
        FilteredJobs.Refresh();
        OnPropertyChanged(nameof(JobCountSummary));
        LatestLog = "Cleared all jobs.";
        RefreshJobCommandStates();
        return Task.CompletedTask;
    }

    private void LoadJobs()
    {
        foreach (var job in _jobRepository.LoadRecent())
        {
            Jobs.Add(job);
        }

        SelectedJob = Jobs.FirstOrDefault();
        OnPropertyChanged(nameof(JobCountSummary));
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
        RefreshJobCommandStates();
        UpdatePlaybackCommandStates();
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
        LatestLog = IsAudioPlaying
            ? $"Playing audio: {audioPath}"
            : "Audio playback paused.";
        return Task.CompletedTask;
    }

    private bool CanPlaySelectedJobAudio()
    {
        return ResolveSelectedJobPlaybackPath() is not null;
    }

    private string? ResolveSelectedJobPlaybackPath()
    {
        if (SelectedJob is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(SelectedJob.NormalizedAudioPath) && File.Exists(SelectedJob.NormalizedAudioPath))
        {
            return SelectedJob.NormalizedAudioPath;
        }

        return File.Exists(SelectedJob.SourceAudioPath) ? SelectedJob.SourceAudioPath : null;
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
    }

    private void RefreshPlaybackState()
    {
        PlaybackDurationSeconds = _audioPlaybackService.Duration.TotalSeconds;

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

    private static string FormatPlaybackTime(TimeSpan value)
    {
        if (value.TotalHours >= 1)
        {
            return value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        return value.ToString(@"mm\:ss", CultureInfo.InvariantCulture);
    }

    private bool FilterJob(object item)
    {
        if (item is not JobSummary job || string.IsNullOrWhiteSpace(JobSearchText))
        {
            return true;
        }

        return job.Title.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase)
            || job.FileName.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase)
            || job.Status.Contains(JobSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedAudioFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".m4a" or ".flac" or ".aac" or ".ogg" or ".opus";
    }
}
