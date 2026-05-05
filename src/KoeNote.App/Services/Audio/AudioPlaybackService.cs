using System.IO;
using System.Windows.Media;

namespace KoeNote.App.Services.Audio;

public interface IAudioPlaybackService
{
    event EventHandler? PlaybackStateChanged;

    bool IsPlaying { get; }

    string? CurrentPath { get; }

    TimeSpan Position { get; }

    TimeSpan Duration { get; }

    bool Toggle(string audioPath);

    bool Open(string audioPath);

    void Seek(TimeSpan position);

    void SetPlaybackRate(double rate);

    void Stop();
}

public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private readonly MediaPlayer _player = new();

    public AudioPlaybackService()
    {
        _player.MediaOpened += (_, _) => PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaFailed += (_, _) => Stop();
    }

    public event EventHandler? PlaybackStateChanged;

    public bool IsPlaying { get; private set; }

    public string? CurrentPath { get; private set; }

    public TimeSpan Position => _player.Position;

    public TimeSpan Duration => _player.NaturalDuration.HasTimeSpan
        ? _player.NaturalDuration.TimeSpan
        : TimeSpan.Zero;

    public bool Toggle(string audioPath)
    {
        if (!Open(audioPath))
        {
            return false;
        }

        return IsPlaying ? Pause() : Play();
    }

    public bool Open(string audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath) || !File.Exists(audioPath))
        {
            Stop();
            return false;
        }

        var fullPath = Path.GetFullPath(audioPath);
        if (string.Equals(CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _player.Open(new Uri(fullPath, UriKind.Absolute));
        CurrentPath = fullPath;
        SetIsPlaying(false);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Seek(TimeSpan position)
    {
        if (CurrentPath is null)
        {
            return;
        }

        if (Duration > TimeSpan.Zero)
        {
            position = TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds, 0, Duration.TotalSeconds));
        }

        _player.Position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPlaybackRate(double rate)
    {
        _player.SpeedRatio = rate > 0 ? rate : 1.0;
    }

    public void Stop()
    {
        if (CurrentPath is not null)
        {
            _player.Stop();
            _player.Close();
            CurrentPath = null;
        }

        SetIsPlaying(false);
    }

    private bool Play()
    {
        _player.Play();
        SetIsPlaying(true);
        return true;
    }

    private bool Pause()
    {
        _player.Pause();
        SetIsPlaying(false);
        return false;
    }

    private void SetIsPlaying(bool isPlaying)
    {
        if (IsPlaying == isPlaying)
        {
            return;
        }

        IsPlaying = isPlaying;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }
}
