using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Tests;

public sealed class SpeakerNameConfirmationPreviewPlaybackControllerTests
{
    [Fact]
    public void Toggle_OpensAudioAndSeeksToPreviewStart()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new SpeakerNameConfirmationPreviewPlaybackController(playback);
        var preview = new SpeakerNameConfirmationPreview(5, 7, "hello");

        var isPlaying = controller.Toggle(audioPath, preview);

        Assert.True(isPlaying);
        Assert.Same(preview, controller.ActivePreview);
        Assert.Equal(audioPath, playback.OpenedPath);
        Assert.Equal(TimeSpan.FromSeconds(5), playback.SeekPosition);
        Assert.True(playback.IsPlaying);
    }

    [Fact]
    public void Refresh_UpdatesProgressAndStopsAtPreviewEnd()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new SpeakerNameConfirmationPreviewPlaybackController(playback);
        var preview = new SpeakerNameConfirmationPreview(5, 7, "hello");

        controller.Toggle(audioPath, preview);
        playback.Position = TimeSpan.FromSeconds(6);

        Assert.True(controller.Refresh());
        Assert.InRange(controller.ProgressPercent, 49, 51);

        playback.Position = TimeSpan.FromSeconds(7);

        Assert.False(controller.Refresh());
        Assert.Null(controller.ActivePreview);
        Assert.False(playback.IsPlaying);
        Assert.Equal(0, playback.StopCount);
        Assert.Equal(audioPath, playback.CurrentPath);
    }

    [Fact]
    public void Toggle_ReturnsFalseWhenAudioPathIsMissing()
    {
        var playback = new FakeAudioPlaybackService();
        var controller = new SpeakerNameConfirmationPreviewPlaybackController(playback);
        var preview = new SpeakerNameConfirmationPreview(5, 7, "hello");

        var isPlaying = controller.Toggle(@"C:\missing\meeting.wav", preview);

        Assert.False(isPlaying);
        Assert.Null(controller.ActivePreview);
        Assert.Null(playback.OpenedPath);
    }

    [Fact]
    public void Toggle_StopsWhenSamePreviewIsAlreadyPlaying()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new SpeakerNameConfirmationPreviewPlaybackController(playback);
        var preview = new SpeakerNameConfirmationPreview(5, 7, "hello");

        Assert.True(controller.Toggle(audioPath, preview));

        var isPlaying = controller.Toggle(audioPath, preview);

        Assert.False(isPlaying);
        Assert.Null(controller.ActivePreview);
        Assert.False(playback.IsPlaying);
        Assert.Equal(0, playback.StopCount);
        Assert.Equal(audioPath, playback.CurrentPath);
    }

    [Fact]
    public void Close_ReleasesUnderlyingPlayback()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new SpeakerNameConfirmationPreviewPlaybackController(playback);
        var preview = new SpeakerNameConfirmationPreview(5, 7, "hello");

        Assert.True(controller.Toggle(audioPath, preview));

        controller.Close();

        Assert.Null(controller.ActivePreview);
        Assert.False(playback.IsPlaying);
        Assert.Null(playback.CurrentPath);
        Assert.Equal(1, playback.StopCount);
    }

    private static string CreateAudioFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "meeting.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "audio");
        return path;
    }

    private sealed class FakeAudioPlaybackService : IAudioPlaybackService
    {
        public event EventHandler? PlaybackStateChanged;

        public bool IsPlaying { get; private set; }

        public string? CurrentPath { get; private set; }

        public TimeSpan Position { get; set; }

        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(60);

        public string? OpenedPath { get; private set; }

        public TimeSpan SeekPosition { get; private set; }

        public int StopCount { get; private set; }

        public bool Toggle(string audioPath)
        {
            CurrentPath = audioPath;
            IsPlaying = !IsPlaying;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return IsPlaying;
        }

        public bool Open(string audioPath)
        {
            OpenedPath = audioPath;
            CurrentPath = audioPath;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Seek(TimeSpan position)
        {
            SeekPosition = position;
            Position = position;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetPlaybackRate(double rate)
        {
        }

        public void SetVolume(double volume)
        {
        }

        public void Stop()
        {
            StopCount++;
            IsPlaying = false;
            CurrentPath = null;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
