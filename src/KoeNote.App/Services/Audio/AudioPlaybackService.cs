using System.IO;
using System.Runtime.InteropServices;
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

    void SetVolume(double volume);

    void Stop();
}

public sealed class AudioPlaybackService : IAudioPlaybackService
{
    private readonly MediaPlayer _player = new();
    private TimeSpan _clockPosition = TimeSpan.Zero;
    private DateTimeOffset? _clockStartedAt;
    private double _playbackRate = 1.0;

    public AudioPlaybackService()
    {
        _player.MediaOpened += (_, _) => PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        _player.MediaEnded += (_, _) => Stop();
        _player.MediaFailed += (_, _) => ContinueWithClockFallback();
    }

    public event EventHandler? PlaybackStateChanged;

    public bool IsPlaying { get; private set; }

    public string? CurrentPath { get; private set; }

    public TimeSpan Position => ClampToDuration(IsPlaying ? EstimatePlaybackPosition() : _clockPosition);

    public TimeSpan Duration
    {
        get
        {
            try
            {
                return _player.NaturalDuration.HasTimeSpan
                    ? _player.NaturalDuration.TimeSpan
                    : TimeSpan.Zero;
            }
            catch (InvalidOperationException)
            {
                return TimeSpan.Zero;
            }
            catch (COMException)
            {
                return TimeSpan.Zero;
            }
        }
    }

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

        try
        {
            _player.Open(new Uri(fullPath, UriKind.Absolute));
        }
        catch (InvalidOperationException)
        {
            // Keep the file selected so the UI clock can still be used on hosts without audio playback support.
        }
        catch (COMException)
        {
            // Keep the file selected so the UI clock can still be used on hosts without audio playback support.
        }

        CurrentPath = fullPath;
        ResetPlaybackClock();
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

        var targetPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        try
        {
            _player.Position = targetPosition;
            _clockPosition = ClampToDuration(ReadPlayerPosition());
        }
        catch (InvalidOperationException)
        {
            _clockPosition = ClampToDuration(targetPosition);
        }
        catch (COMException)
        {
            _clockPosition = ClampToDuration(targetPosition);
        }

        _clockStartedAt = IsPlaying ? DateTimeOffset.UtcNow : null;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetPlaybackRate(double rate)
    {
        _clockPosition = Position;
        _clockStartedAt = IsPlaying ? DateTimeOffset.UtcNow : null;
        _playbackRate = rate > 0 ? rate : 1.0;
        try
        {
            _player.SpeedRatio = _playbackRate;
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }
    }

    public void SetVolume(double volume)
    {
        try
        {
            _player.Volume = Math.Clamp(volume, 0, 1);
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        if (CurrentPath is not null)
        {
            try
            {
                _player.Stop();
                _player.Close();
            }
            catch (InvalidOperationException)
            {
            }
            catch (COMException)
            {
            }

            CurrentPath = null;
        }

        ResetPlaybackClock();
        SetIsPlaying(false);
    }

    private bool Play()
    {
        var playerPosition = ReadPlayerPosition();
        _clockPosition = ClampToDuration(playerPosition > TimeSpan.Zero ? playerPosition : _clockPosition);
        _clockStartedAt = DateTimeOffset.UtcNow;
        try
        {
            _player.Play();
        }
        catch (InvalidOperationException)
        {
            // Continue with the UI playback clock when the host cannot render audio.
        }
        catch (COMException)
        {
            // Continue with the UI playback clock when the host cannot render audio.
        }

        SetIsPlaying(true);
        return true;
    }

    private bool Pause()
    {
        _clockPosition = Position;
        _clockStartedAt = null;
        try
        {
            _player.Pause();
        }
        catch (InvalidOperationException)
        {
        }
        catch (COMException)
        {
        }

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

    private TimeSpan EstimatePlaybackPosition()
    {
        if (_clockStartedAt is null)
        {
            return ClampToDuration(_clockPosition);
        }

        var elapsed = DateTimeOffset.UtcNow - _clockStartedAt.Value;
        var estimatedPosition = _clockPosition + TimeSpan.FromSeconds(elapsed.TotalSeconds * _playbackRate);
        var playerPosition = ReadPlayerPosition();
        var position = ClampToDuration(playerPosition > estimatedPosition ? playerPosition : estimatedPosition);
        if (Duration > TimeSpan.Zero && position >= Duration)
        {
            _clockPosition = Duration;
            _clockStartedAt = null;
            SetIsPlaying(false);
        }

        return position;
    }

    private TimeSpan ClampToDuration(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var duration = Duration;
        return duration > TimeSpan.Zero && position > duration ? duration : position;
    }

    private void ResetPlaybackClock()
    {
        _clockPosition = TimeSpan.Zero;
        _clockStartedAt = null;
    }

    private void ContinueWithClockFallback()
    {
        var playerPosition = ReadPlayerPosition();
        if (playerPosition > _clockPosition)
        {
            _clockPosition = ClampToDuration(playerPosition);
        }

        if (IsPlaying && _clockStartedAt is null)
        {
            _clockStartedAt = DateTimeOffset.UtcNow;
        }

        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private TimeSpan ReadPlayerPosition()
    {
        try
        {
            return _player.Position;
        }
        catch (InvalidOperationException)
        {
            return TimeSpan.Zero;
        }
        catch (COMException)
        {
            return TimeSpan.Zero;
        }
    }
}
