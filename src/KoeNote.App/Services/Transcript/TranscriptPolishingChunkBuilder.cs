namespace KoeNote.App.Services.Transcript;

internal static class TranscriptPolishingChunkBuilder
{
    public static IEnumerable<TranscriptPolishingChunk> BuildChunks(
        IReadOnlyList<TranscriptReadModel> segments,
        int chunkSegmentCount)
    {
        var chunkIndex = 1;
        var currentChunk = new List<TranscriptReadModel>();

        foreach (var speakerBlock in BuildSpeakerBlocks(segments))
        {
            if (speakerBlock.Count > chunkSegmentCount)
            {
                if (currentChunk.Count > 0)
                {
                    yield return new TranscriptPolishingChunk(chunkIndex++, currentChunk.ToArray());
                    currentChunk.Clear();
                }

                for (var index = 0; index < speakerBlock.Count; index += chunkSegmentCount)
                {
                    yield return new TranscriptPolishingChunk(
                        chunkIndex++,
                        speakerBlock.Skip(index).Take(chunkSegmentCount).ToArray());
                }

                continue;
            }

            if (currentChunk.Count > 0 && currentChunk.Count + speakerBlock.Count > chunkSegmentCount)
            {
                yield return new TranscriptPolishingChunk(chunkIndex++, currentChunk.ToArray());
                currentChunk.Clear();
            }

            currentChunk.AddRange(speakerBlock);
        }

        if (currentChunk.Count > 0)
        {
            yield return new TranscriptPolishingChunk(chunkIndex, currentChunk.ToArray());
        }
    }

    public static IEnumerable<IReadOnlyList<TranscriptReadModel>> BuildSpeakerBlocks(
        IReadOnlyList<TranscriptReadModel> segments)
    {
        var currentBlock = new List<TranscriptReadModel>();
        var currentSpeaker = string.Empty;

        foreach (var segment in segments)
        {
            if (currentBlock.Count > 0 && !string.Equals(currentSpeaker, segment.Speaker, StringComparison.Ordinal))
            {
                yield return currentBlock.ToArray();
                currentBlock.Clear();
            }

            currentSpeaker = segment.Speaker;
            currentBlock.Add(segment);
        }

        if (currentBlock.Count > 0)
        {
            yield return currentBlock.ToArray();
        }
    }

    public static string BuildSegmentRange(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments.Count == 0 ? string.Empty : $"{segments[0].SegmentId}..{segments[^1].SegmentId}";
    }
}
