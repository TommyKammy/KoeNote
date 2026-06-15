using System.IO;
using KoeNote.App.Services.Audio;

namespace KoeNote.App.Services.Dialogs;

public sealed record AudioPreviewRange(double StartSeconds, double EndSeconds, string? Key = null);

public sealed class AudioPreviewPlaybackController(IAudioPlaybackService audioPlaybackService)
{
    private const double FallbackPreviewSeconds = 3.0;

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
            Stop();
            return false;
        }

        if (ActivePreview == preview && audioPlaybackService.IsPlaying)
        {
            Stop();
            return false;
        }

        Stop();
        if (!audioPlaybackService.Open(audioPath!))
        {
            return false;
        }

        audioPlaybackService.Seek(TimeSpan.FromSeconds(SanitizeSeconds(preview.StartSeconds)));
        ActivePreview = preview;
        var isPlaying = audioPlaybackService.Toggle(audioPath!);
        if (!isPlaying)
        {
            ActivePreview = null;
            ProgressPercent = 0;
            return false;
        }

        Refresh();
        return true;
    }

    public void Stop()
    {
        audioPlaybackService.Stop();
        ActivePreview = null;
        ProgressPercent = 0;
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

        var durationSeconds = Math.Max(0.1, endSeconds - startSeconds);
        ProgressPercent = Math.Clamp((positionSeconds - startSeconds) / durationSeconds * 100, 0, 100);
        return audioPlaybackService.IsPlaying;
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
}
