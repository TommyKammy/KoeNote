namespace KoeNote.App.Services.Audio;

internal static class AudioPlaybackStateCalculator
{
    public static double NormalizePlaybackRate(double rate)
    {
        return rate > 0 ? rate : 1.0;
    }

    public static TimeSpan ClampToDuration(TimeSpan position, TimeSpan duration)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return duration > TimeSpan.Zero && position > duration ? duration : position;
    }

    public static TimeSpan SelectPlaybackStartPosition(
        TimeSpan playerPosition,
        TimeSpan clockPosition,
        TimeSpan duration)
    {
        var position = playerPosition > TimeSpan.Zero ? playerPosition : clockPosition;
        return ClampToDuration(position, duration);
    }

    public static TimeSpan EstimatePosition(
        TimeSpan clockPosition,
        DateTimeOffset? clockStartedAt,
        DateTimeOffset now,
        double playbackRate,
        TimeSpan playerPosition,
        TimeSpan duration)
    {
        if (clockStartedAt is null)
        {
            return ClampToDuration(clockPosition, duration);
        }

        var normalizedRate = NormalizePlaybackRate(playbackRate);
        var elapsed = now - clockStartedAt.Value;
        var estimatedPosition = clockPosition + TimeSpan.FromSeconds(elapsed.TotalSeconds * normalizedRate);
        var position = playerPosition > estimatedPosition ? playerPosition : estimatedPosition;
        return ClampToDuration(position, duration);
    }

    public static bool HasReachedDuration(TimeSpan position, TimeSpan duration)
    {
        return duration > TimeSpan.Zero && position >= duration;
    }
}
