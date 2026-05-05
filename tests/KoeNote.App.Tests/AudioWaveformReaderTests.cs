using KoeNote.App.Services.Audio;

namespace KoeNote.App.Tests;

public sealed class AudioWaveformReaderTests
{
    [Fact]
    public void ReadPeaks_ReturnsNormalizedPeaksForPcmWav()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wavPath = Path.Combine(root, "tone.wav");
        WriteMono16BitWav(wavPath, [0, 1000, -4000, 12000, -20000, 28000, -12000, 2000]);

        var peaks = AudioWaveformReader.ReadPeaks(wavPath, peakCount: 4);

        Assert.Equal(4, peaks.Count);
        Assert.All(peaks, peak => Assert.InRange(peak, 0.04, 1.0));
        Assert.Equal(1.0, peaks.Max(), precision: 3);
    }

    [Fact]
    public void ReadPeaks_ReturnsEmptyForUnsupportedAudio()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var audioPath = Path.Combine(root, "audio.mp3");
        File.WriteAllText(audioPath, "not a wav");

        var peaks = AudioWaveformReader.ReadPeaks(audioPath);

        Assert.Empty(peaks);
    }

    [Fact]
    public void ReadPeaks_ReturnsEmptyForMalformedWav()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var wavPath = Path.Combine(root, "broken.wav");
        File.WriteAllBytes(wavPath, "RIFF\x04\0\0\0WAVEfmt "u8.ToArray());

        var peaks = AudioWaveformReader.ReadPeaks(wavPath);

        Assert.Empty(peaks);
    }

    private static void WriteMono16BitWav(string path, short[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        var dataSize = samples.Length * sizeof(short);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(24000);
        writer.Write(24000 * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }
}
