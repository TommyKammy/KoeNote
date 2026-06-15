using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Tests;

public sealed class AudioPreviewPlaybackControllerTests
{
    [Fact]
    public void Toggle_OpensAudioAndSeeksToPreviewStart()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 7, "segment-1");

        var isPlaying = controller.Toggle(audioPath, preview);

        Assert.True(isPlaying);
        Assert.Equal(preview, controller.ActivePreview);
        Assert.Equal(audioPath, playback.OpenedPath);
        Assert.Equal(TimeSpan.FromSeconds(5), playback.SeekPosition);
        Assert.True(playback.IsPlaying);
    }

    [Fact]
    public void Refresh_UpdatesProgressAndStopsAtPreviewEnd()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 7, "segment-1");

        controller.Toggle(audioPath, preview);
        playback.Position = TimeSpan.FromSeconds(6);

        Assert.True(controller.Refresh());
        Assert.InRange(controller.ProgressPercent, 49, 51);

        playback.Position = TimeSpan.FromSeconds(7);

        Assert.False(controller.Refresh());
        Assert.Null(controller.ActivePreview);
        Assert.False(playback.IsPlaying);
        Assert.True(playback.StopCount >= 1);
    }

    [Fact]
    public void Toggle_ReturnsFalseWhenAudioPathIsMissing()
    {
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 7, "segment-1");

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
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 7, "segment-1");

        Assert.True(controller.Toggle(audioPath, preview));

        var isPlaying = controller.Toggle(audioPath, preview);

        Assert.False(isPlaying);
        Assert.Null(controller.ActivePreview);
        Assert.False(playback.IsPlaying);
    }

    [Fact]
    public void Toggle_OpensDifferentAudioWhenSamePreviewIsPlayingForAnotherFile()
    {
        var audioPath = CreateAudioFile();
        var otherAudioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 7, "segment-1");

        Assert.True(controller.Toggle(audioPath, preview));

        var isPlaying = controller.Toggle(otherAudioPath, preview);

        Assert.True(isPlaying);
        Assert.Equal(preview, controller.ActivePreview);
        Assert.Equal(Path.GetFullPath(otherAudioPath), controller.ActiveAudioPath);
        Assert.Equal(otherAudioPath, playback.OpenedPath);
        Assert.True(playback.IsPlaying);
    }

    [Fact]
    public void Refresh_ClearsStateWhenPlaybackStopsBeforePreviewEnd()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 10, "segment-1");

        controller.Toggle(audioPath, preview);
        playback.Position = TimeSpan.FromSeconds(6);
        Assert.True(controller.Refresh());
        Assert.True(controller.ProgressPercent > 0);

        playback.Stop();

        Assert.False(controller.Refresh());
        Assert.Null(controller.ActivePreview);
        Assert.Null(controller.ActiveAudioPath);
        Assert.Equal(0, controller.ProgressPercent);
    }

    [Fact]
    public void Toggle_SanitizesInvalidStartSeconds()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(double.NaN, 7, "segment-1");

        var isPlaying = controller.Toggle(audioPath, preview);

        Assert.True(isPlaying);
        Assert.Equal(TimeSpan.Zero, playback.SeekPosition);
    }

    [Fact]
    public void Refresh_UsesFallbackDurationWhenEndIsBeforeStart()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var controller = new AudioPreviewPlaybackController(playback);
        var preview = new AudioPreviewRange(5, 4, "segment-1");

        controller.Toggle(audioPath, preview);
        playback.Position = TimeSpan.FromSeconds(7.9);

        Assert.True(controller.Refresh());

        playback.Position = TimeSpan.FromSeconds(8);

        Assert.False(controller.Refresh());
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
