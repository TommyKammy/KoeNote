using System.IO;
using KoeNote.App.Services.Audio;

namespace KoeNote.App.Services.Dialogs;

public sealed record AudioPreviewRange(double StartSeconds, double EndSeconds, string? Key = null);

public sealed class AudioPreviewPlaybackController(IAudioPlaybackService audioPlaybackService)
{
    private const double FallbackPreviewSeconds = 3.0;

    public string? ActiveAudioPath { get; private set; }

    public AudioPreviewRange? ActivePreview { get; private set; }

    public bool IsPlaying => ActivePreview is not null && audioPlaybackService.IsPlaying;

    public double ProgressPercent { get; private set; }

    public bool CanPlay(string? audioPath)
    {
        return !string.IsNullOrWhiteSpace(audioPath) && File.Exists(audioPath);
    }

    public bool Toggle(string? audioPath, AudioPreviewRange preview)
    {
        if (!CanPlay(audioPath))
        {
            Close();
            return false;
        }

        var fullPath = Path.GetFullPath(audioPath!);
        if (ActivePreview == preview &&
            string.Equals(ActiveAudioPath, fullPath, StringComparison.OrdinalIgnoreCase) &&
            audioPlaybackService.IsPlaying)
        {
            Stop();
            return false;
        }

        Stop();
        if (!audioPlaybackService.Open(audioPath!))
        {
            Close();
            return false;
        }

        audioPlaybackService.Seek(TimeSpan.FromSeconds(SanitizeSeconds(preview.StartSeconds)));
        ActivePreview = preview;
        ActiveAudioPath = fullPath;
        var isPlaying = audioPlaybackService.Toggle(audioPath!);
        if (!isPlaying)
        {
            ClearState();
            return false;
        }

        Refresh();
        return true;
    }

    public void Stop()
    {
        if (ActiveAudioPath is not null && audioPlaybackService.IsPlaying)
        {
            audioPlaybackService.Toggle(ActiveAudioPath);
        }

        ClearState();
    }

    public void Close()
    {
        audioPlaybackService.Stop();
        ClearState();
    }

    public bool Refresh()
    {
        if (ActivePreview is not { } preview)
        {
            ProgressPercent = 0;
            return false;
        }

        var startSeconds = SanitizeSeconds(preview.StartSeconds);
        var endSeconds = GetPreviewEndSeconds(preview);
        var positionSeconds = audioPlaybackService.Position.TotalSeconds;
        if (positionSeconds >= endSeconds)
        {
            Stop();
            return false;
        }

        if (!audioPlaybackService.IsPlaying)
        {
            ClearState();
            return false;
        }

        var durationSeconds = Math.Max(0.1, endSeconds - startSeconds);
        ProgressPercent = Math.Clamp((positionSeconds - startSeconds) / durationSeconds * 100, 0, 100);
        return true;
    }

    private static double GetPreviewEndSeconds(AudioPreviewRange preview)
    {
        var startSeconds = SanitizeSeconds(preview.StartSeconds);
        var endSeconds = SanitizeSeconds(preview.EndSeconds);
        return endSeconds > startSeconds ? endSeconds : startSeconds + FallbackPreviewSeconds;
    }

    private static double SanitizeSeconds(double seconds)
    {
        return seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds) ? 0 : seconds;
    }

    private void ClearState()
    {
        ActiveAudioPath = null;
        ActivePreview = null;
        ProgressPercent = 0;
    }
}
