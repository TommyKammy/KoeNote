using System.IO;

namespace KoeNote.App.Services.Diarization;

public static class DiarizationTimeoutPolicy
{
    private static readonly TimeSpan MinimumTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromMinutes(15);
    private const double NormalizedWavBytesPerSecond = 24_000 * 2;
    private const double TimeoutRatio = 0.10;
    private const long WavHeaderBytes = 44;

    public static TimeSpan Estimate(string normalizedAudioPath)
    {
        if (!File.Exists(normalizedAudioPath))
        {
            return MinimumTimeout;
        }

        var audioBytes = Math.Max(0, new FileInfo(normalizedAudioPath).Length - WavHeaderBytes);
        var audioSeconds = audioBytes / NormalizedWavBytesPerSecond;
        var timeout = TimeSpan.FromSeconds(audioSeconds * TimeoutRatio);

        if (timeout < MinimumTimeout)
        {
            return MinimumTimeout;
        }

        return timeout > MaximumTimeout ? MaximumTimeout : timeout;
    }
}
