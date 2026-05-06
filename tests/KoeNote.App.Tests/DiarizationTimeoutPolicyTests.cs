using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Tests;

public sealed class DiarizationTimeoutPolicyTests
{
    [Fact]
    public void Estimate_UsesMinimumForMissingAudio()
    {
        var timeout = DiarizationTimeoutPolicy.Estimate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.wav"));

        Assert.Equal(TimeSpan.FromMinutes(2), timeout);
    }

    [Fact]
    public void Estimate_ScalesFromNormalizedWavLength()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "audio.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            stream.SetLength(44 + (long)(24_000 * 2 * TimeSpan.FromHours(2).TotalSeconds));
        }

        var timeout = DiarizationTimeoutPolicy.Estimate(path);

        Assert.Equal(TimeSpan.FromMinutes(12), timeout);
    }

    [Fact]
    public void Estimate_CapsVeryLongAudio()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "audio.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        {
            stream.SetLength(44 + (long)(24_000 * 2 * TimeSpan.FromHours(10).TotalSeconds));
        }

        var timeout = DiarizationTimeoutPolicy.Estimate(path);

        Assert.Equal(TimeSpan.FromMinutes(15), timeout);
    }
}
