using System.IO;
using System.Text;

namespace KoeNote.App.Services.Asr;

public sealed record WavAudioChunk(
    int Index,
    string AudioPath,
    double OffsetSeconds,
    double DurationSeconds);

public sealed class WavAudioChunker
{
    public IReadOnlyList<WavAudioChunk> SplitLongWav(
        string sourcePath,
        string outputDirectory,
        int chunkSeconds)
    {
        if (chunkSeconds <= 0 || !File.Exists(sourcePath))
        {
            return [];
        }

        if (!TryReadLayout(sourcePath, out var layout))
        {
            return [];
        }

        var sourceDurationSeconds = layout.DataSize / (double)layout.ByteRate;
        if (sourceDurationSeconds <= chunkSeconds)
        {
            return [];
        }

        Directory.CreateDirectory(outputDirectory);
        var maxChunkBytes = AlignDown((long)chunkSeconds * layout.ByteRate, layout.BlockAlign);
        if (maxChunkBytes <= 0)
        {
            return [];
        }

        var chunks = new List<WavAudioChunk>();
        using var source = File.OpenRead(sourcePath);
        var remainingBytes = layout.DataSize;
        var sourceOffsetBytes = 0L;
        var index = 1;

        while (remainingBytes > 0)
        {
            var chunkDataBytes = Math.Min(maxChunkBytes, remainingBytes);
            chunkDataBytes = AlignDown(chunkDataBytes, layout.BlockAlign);
            if (chunkDataBytes <= 0)
            {
                break;
            }

            var chunkPath = Path.Combine(outputDirectory, $"chunk-{index:D3}.wav");
            source.Position = layout.DataOffset + sourceOffsetBytes;
            WriteChunk(source, chunkPath, layout.FormatBytes, chunkDataBytes);

            chunks.Add(new WavAudioChunk(
                index,
                chunkPath,
                sourceOffsetBytes / (double)layout.ByteRate,
                chunkDataBytes / (double)layout.ByteRate));

            sourceOffsetBytes += chunkDataBytes;
            remainingBytes -= chunkDataBytes;
            index++;
        }

        return chunks.Count > 1 ? chunks : [];
    }

    private static bool TryReadLayout(string sourcePath, out WavLayout layout)
    {
        layout = default;
        try
        {
            using var stream = File.OpenRead(sourcePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            if (ReadFourCc(reader) != "RIFF")
            {
                return false;
            }

            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
            {
                return false;
            }

            byte[]? formatBytes = null;
            long dataOffset = 0;
            long dataSize = 0;
            ushort blockAlign = 0;
            uint byteRate = 0;

            while (stream.Position + 8 <= stream.Length)
            {
                var chunkId = ReadFourCc(reader);
                var chunkSize = reader.ReadUInt32();
                var chunkDataStart = stream.Position;

                if (chunkId == "fmt ")
                {
                    formatBytes = reader.ReadBytes(checked((int)chunkSize));
                    if (formatBytes.Length < 16)
                    {
                        return false;
                    }

                    using var formatStream = new MemoryStream(formatBytes);
                    using var formatReader = new BinaryReader(formatStream);
                    _ = formatReader.ReadUInt16();
                    _ = formatReader.ReadUInt16();
                    _ = formatReader.ReadUInt32();
                    byteRate = formatReader.ReadUInt32();
                    blockAlign = formatReader.ReadUInt16();
                }
                else if (chunkId == "data")
                {
                    dataOffset = stream.Position;
                    dataSize = chunkSize;
                    stream.Position += chunkSize;
                }
                else
                {
                    stream.Position += chunkSize;
                }

                if ((chunkSize & 1) == 1 && stream.Position < stream.Length)
                {
                    stream.Position++;
                }

                if (stream.Position < chunkDataStart)
                {
                    return false;
                }
            }

            if (formatBytes is null || dataOffset <= 0 || dataSize <= 0 || blockAlign <= 0 || byteRate == 0)
            {
                return false;
            }

            layout = new WavLayout(formatBytes, dataOffset, dataSize, blockAlign, byteRate);
            return true;
        }
        catch (EndOfStreamException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static void WriteChunk(Stream source, string chunkPath, byte[] formatBytes, long dataBytes)
    {
        using var destination = File.Create(chunkPath);
        using var writer = new BinaryWriter(destination, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(checked((uint)(4 + 8 + formatBytes.Length + 8 + dataBytes + (dataBytes & 1))));
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write((uint)formatBytes.Length);
        writer.Write(formatBytes);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(checked((uint)dataBytes));

        var buffer = new byte[64 * 1024];
        var remaining = dataBytes;
        while (remaining > 0)
        {
            var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                throw new EndOfStreamException("Unexpected end of WAV data while writing an ASR chunk.");
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }

        if ((dataBytes & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }

    private static long AlignDown(long value, ushort blockAlign)
    {
        return value - (value % blockAlign);
    }

    private readonly record struct WavLayout(
        byte[] FormatBytes,
        long DataOffset,
        long DataSize,
        ushort BlockAlign,
        uint ByteRate);
}
