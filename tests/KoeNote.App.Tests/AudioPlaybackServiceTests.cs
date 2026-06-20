using KoeNote.App.Services.Audio;

namespace KoeNote.App.Tests;

public sealed class AudioPlaybackServiceTests
{
    [Fact]
    public void SeekBeforeMediaOpened_StartsClockAtRequestedPositionAndReappliesWhenOpened()
    {
        var audioPath = CreateAudioFile();
        var player = new FakeAudioMediaPlayer();
        var service = new AudioPlaybackService(player);

        Assert.True(service.Open(audioPath));
        service.Seek(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), service.Position);
        Assert.Equal(TimeSpan.Zero, player.Position);

        Assert.True(service.Toggle(audioPath));
        Assert.True(service.IsPlaying);
        Assert.InRange(service.Position.TotalSeconds, 5, 6);
        Assert.Equal(TimeSpan.Zero, player.Position);

        player.RaiseMediaOpened();

        Assert.Equal(TimeSpan.FromSeconds(5), player.Position);
        Assert.InRange(service.Position.TotalSeconds, 5, 6);
    }

    [Fact]
    public void SeekAfterMediaOpened_DoesNotReapplyStalePositionWhenPlaybackResumes()
    {
        var audioPath = CreateAudioFile();
        var player = new FakeAudioMediaPlayer();
        var service = new AudioPlaybackService(player);

        Assert.True(service.Open(audioPath));
        player.RaiseMediaOpened();
        service.Seek(TimeSpan.FromSeconds(5));
        Assert.Equal([TimeSpan.FromSeconds(5)], player.PositionSetRequests);

        Assert.True(service.Toggle(audioPath));
        player.SetPlaybackPosition(TimeSpan.FromSeconds(6));
        Assert.False(service.Toggle(audioPath));

        Assert.True(service.Toggle(audioPath));

        Assert.Equal([TimeSpan.FromSeconds(5)], player.PositionSetRequests);
        Assert.InRange(service.Position.TotalSeconds, 6, 7);
    }

    private static string CreateAudioFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "meeting.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "audio");
        return path;
    }

    private sealed class FakeAudioMediaPlayer : IAudioMediaPlayer
    {
        private TimeSpan _position = TimeSpan.Zero;
        private bool _isOpened;

        public event EventHandler? MediaOpened;

        public event EventHandler? MediaEnded;

        public event EventHandler? MediaFailed;

        public bool HasDuration => false;

        public TimeSpan Duration => TimeSpan.Zero;

        public TimeSpan Position
        {
            get => _position;
            set
            {
                PositionSetRequests.Add(value);
                if (_isOpened)
                {
                    _position = value;
                }
            }
        }

        public List<TimeSpan> PositionSetRequests { get; } = [];

        public double SpeedRatio { private get; set; }

        public double Volume { private get; set; }

        public int PlayCount { get; private set; }

        public void Open(Uri source)
        {
        }

        public void Play()
        {
            PlayCount++;
        }

        public void Pause()
        {
        }

        public void Stop()
        {
            _isOpened = false;
            _position = TimeSpan.Zero;
        }

        public void Close()
        {
        }

        public void RaiseMediaOpened()
        {
            _isOpened = true;
            MediaOpened?.Invoke(this, EventArgs.Empty);
        }

        public void SetPlaybackPosition(TimeSpan position)
        {
            _position = position;
        }

        public void RaiseMediaEnded()
        {
            MediaEnded?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseMediaFailed()
        {
            MediaFailed?.Invoke(this, EventArgs.Empty);
        }
    }
}
