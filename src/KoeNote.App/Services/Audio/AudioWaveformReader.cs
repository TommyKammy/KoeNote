using System.IO;

namespace KoeNote.App.Services.Audio;

public static class AudioWaveformReader
{
    private const int MaxFramesPerPeak = 4096;

    public static IReadOnlyList<double> ReadPeaks(string audioPath, int peakCount = 96)
    {
        if (peakCount <= 0 || !File.Exists(audioPath) || !Path.GetExtension(audioPath).Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            using var stream = File.OpenRead(audioPath);
            using var reader = new BinaryReader(stream);
            if (ReadAscii(reader, 4) != "RIFF")
            {
                return [];
            }

            reader.ReadUInt32();
            if (ReadAscii(reader, 4) != "WAVE")
            {
                return [];
            }

            WaveFormat? format = null;
            long dataOffset = -1;
            uint dataSize = 0;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadAscii(reader, 4);
                var chunkSize = reader.ReadUInt32();
                var chunkStart = stream.Position;

                if (chunkId == "fmt ")
                {
                    format = ReadFormat(reader, chunkSize);
                }
                else if (chunkId == "data")
                {
                    dataOffset = chunkStart;
                    dataSize = chunkSize;
                }

                stream.Position = Math.Min(stream.Length, chunkStart + chunkSize + (chunkSize % 2));
                if (format is not null && dataOffset >= 0)
                {
                    break;
                }
            }

            if (format is null || dataOffset < 0 || format.Channels <= 0 || format.BitsPerSample <= 0)
            {
                return [];
            }

            var bytesPerSample = format.BitsPerSample / 8;
            var frameSize = bytesPerSample * format.Channels;
            if (bytesPerSample <= 0 || frameSize <= 0 || dataSize < frameSize)
            {
                return [];
            }

            var frameCount = dataSize / frameSize;
            var framesPerPeak = Math.Max(1, frameCount / peakCount);
            var peaks = new List<double>(peakCount);
            var maxPeak = 0.0;

            for (var peakIndex = 0; peakIndex < peakCount; peakIndex++)
            {
                var startFrame = peakIndex * framesPerPeak;
                if (startFrame >= frameCount)
                {
                    peaks.Add(0);
                    continue;
                }

                var endFrame = Math.Min(frameCount, peakIndex == peakCount - 1 ? frameCount : startFrame + framesPerPeak);
                var stride = Math.Max(1, (endFrame - startFrame) / MaxFramesPerPeak);
                var peak = 0.0;
                for (var frame = startFrame; frame < endFrame; frame += stride)
                {
                    stream.Position = dataOffset + frame * frameSize;
                    for (var channel = 0; channel < format.Channels; channel++)
                    {
                        peak = Math.Max(peak, Math.Abs(ReadSample(reader, format.BitsPerSample, format.FormatTag)));
                    }
                }

                peaks.Add(peak);
                maxPeak = Math.Max(maxPeak, peak);
            }

            if (maxPeak <= 0)
            {
                return peaks;
            }

            for (var i = 0; i < peaks.Count; i++)
            {
                peaks[i] = Math.Clamp(peaks[i] / maxPeak, 0.04, 1.0);
            }

            return peaks;
        }
        catch (EndOfStreamException)
        {
            return [];
        }
        catch (InvalidDataException)
        {
            return [];
        }
        catch (IOException)
        {
            return [];
        }
        catch (OverflowException)
        {
            return [];
        }
    }

    private static WaveFormat ReadFormat(BinaryReader reader, uint chunkSize)
    {
        if (chunkSize < 16)
        {
            throw new InvalidDataException("WAV fmt chunk is too short.");
        }

        var formatTag = reader.ReadUInt16();
        var channels = reader.ReadUInt16();
        reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt16();
        var bitsPerSample = reader.ReadUInt16();
        if (chunkSize > 16)
        {
            reader.ReadBytes(checked((int)chunkSize - 16));
        }

        return new WaveFormat(formatTag, channels, bitsPerSample);
    }

    private static double ReadSample(BinaryReader reader, int bitsPerSample, ushort formatTag)
    {
        return formatTag switch
        {
            1 => ReadPcmSample(reader, bitsPerSample),
            3 when bitsPerSample == 32 => reader.ReadSingle(),
            _ => 0
        };
    }

    private static double ReadPcmSample(BinaryReader reader, int bitsPerSample)
    {
        return bitsPerSample switch
        {
            8 => (reader.ReadByte() - 128) / 128.0,
            16 => reader.ReadInt16() / 32768.0,
            24 => ReadInt24LittleEndian(reader) / 8388608.0,
            32 => reader.ReadInt32() / 2147483648.0,
            _ => 0
        };
    }

    private static int ReadInt24LittleEndian(BinaryReader reader)
    {
        var data = reader.ReadBytes(3);
        if (data.Length < 3)
        {
            throw new EndOfStreamException();
        }

        var value = data[0] | (data[1] << 8) | (data[2] << 16);
        return (value & 0x800000) != 0 ? value | unchecked((int)0xFF000000) : value;
    }

    private static string ReadAscii(BinaryReader reader, int count)
    {
        return System.Text.Encoding.ASCII.GetString(reader.ReadBytes(count));
    }

    private sealed record WaveFormat(ushort FormatTag, ushort Channels, ushort BitsPerSample);
}
