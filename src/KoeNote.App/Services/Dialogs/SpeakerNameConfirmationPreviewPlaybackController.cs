using KoeNote.App.Services.Audio;

namespace KoeNote.App.Services.Dialogs;

public sealed class SpeakerNameConfirmationPreviewPlaybackController
{
    private readonly AudioPreviewPlaybackController _playbackController;

    public SpeakerNameConfirmationPreviewPlaybackController(IAudioPlaybackService audioPlaybackService)
    {
        _playbackController = new AudioPreviewPlaybackController(audioPlaybackService);
    }

    public SpeakerNameConfirmationPreview? ActivePreview { get; private set; }

    public bool IsPlaying => ActivePreview is not null && _playbackController.IsPlaying;

    public double ProgressPercent => _playbackController.ProgressPercent;

    public bool CanPlay(string? audioPath)
    {
        return _playbackController.CanPlay(audioPath);
    }

    public bool Toggle(string? audioPath, SpeakerNameConfirmationPreview preview)
    {
        var isPlaying = _playbackController.Toggle(audioPath, ToAudioPreviewRange(preview));
        ActivePreview = isPlaying ? preview : null;
        return isPlaying;
    }

    public void Stop()
    {
        _playbackController.Stop();
        ActivePreview = null;
    }

    public void Close()
    {
        _playbackController.Close();
        ActivePreview = null;
    }

    public bool Refresh()
    {
        var isPlaying = _playbackController.Refresh();
        if (!isPlaying)
        {
            ActivePreview = null;
        }

        return isPlaying;
    }

    private static AudioPreviewRange ToAudioPreviewRange(SpeakerNameConfirmationPreview preview)
    {
        return new AudioPreviewRange(preview.StartSeconds, preview.EndSeconds, preview.Text);
    }
}
