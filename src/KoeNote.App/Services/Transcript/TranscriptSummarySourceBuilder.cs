using System.Text;

namespace KoeNote.App.Services.Transcript;

internal static class TranscriptSummarySourceBuilder
{
    public static TranscriptSummarySource ResolveSource(
        string jobId,
        IReadOnlyList<TranscriptReadModel> segments,
        int chunkSegmentCount,
        TranscriptDerivativeRepository derivativeRepository)
    {
        var polished = derivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished);
        if (polished is not null && !derivativeRepository.IsStale(polished))
        {
            return BuildPolishedSource(polished, segments, derivativeRepository);
        }

        return new TranscriptSummarySource(
            TranscriptDerivativeSourceKinds.Raw,
            BuildRawChunks(segments, chunkSegmentCount).ToArray());
    }

    public static bool IsUnexpectedlyShort(
        string finalSummary,
        IReadOnlyList<TranscriptSummaryChunk> sourceChunks)
    {
        var sourceLength = sourceChunks.Sum(static chunk => chunk.Content.Length);
        return sourceLength >= 1000 && finalSummary.Trim().Length < 80;
    }

    private static TranscriptSummarySource BuildPolishedSource(
        TranscriptDerivative polished,
        IReadOnlyList<TranscriptReadModel> segments,
        TranscriptDerivativeRepository derivativeRepository)
    {
        var polishedChunks = derivativeRepository.ReadChunks(polished.DerivativeId)
            .Where(static chunk => string.Equals(chunk.Status, TranscriptDerivativeStatuses.Succeeded, StringComparison.Ordinal))
            .OrderBy(static chunk => chunk.ChunkIndex)
            .ToArray();

        if (polishedChunks.Length > 0)
        {
            return new TranscriptSummarySource(
                TranscriptDerivativeSourceKinds.Polished,
                polishedChunks.Select(static chunk => new TranscriptSummaryChunk(
                    chunk.ChunkIndex,
                    TranscriptDerivativeSourceKinds.Polished,
                    chunk.SourceSegmentIds,
                    chunk.SourceStartSeconds,
                    chunk.SourceEndSeconds,
                    chunk.Content)).ToArray());
        }

        return new TranscriptSummarySource(
            TranscriptDerivativeSourceKinds.Polished,
            [
                new TranscriptSummaryChunk(
                    1,
                    TranscriptDerivativeSourceKinds.Polished,
                    string.Join(",", segments.Select(static segment => segment.SegmentId)),
                    segments.Min(static segment => segment.StartSeconds),
                    segments.Max(static segment => segment.EndSeconds),
                    polished.Content)
            ]);
    }

    private static IEnumerable<TranscriptSummaryChunk> BuildRawChunks(
        IReadOnlyList<TranscriptReadModel> segments,
        int chunkSegmentCount)
    {
        for (var index = 0; index < segments.Count; index += chunkSegmentCount)
        {
            var chunkSegments = segments.Skip(index).Take(chunkSegmentCount).ToArray();
            yield return new TranscriptSummaryChunk(
                (index / chunkSegmentCount) + 1,
                TranscriptDerivativeSourceKinds.Raw,
                string.Join(",", chunkSegments.Select(static segment => segment.SegmentId)),
                chunkSegments.Min(static segment => segment.StartSeconds),
                chunkSegments.Max(static segment => segment.EndSeconds),
                BuildRawChunkContent(chunkSegments));
        }
    }

    private static string BuildRawChunkContent(IReadOnlyList<TranscriptReadModel> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            builder
                .Append("- segment_id: ").Append(segment.SegmentId).AppendLine()
                .Append("  timestamp: ").Append(TranscriptSummaryFallbackBuilder.FormatTimestamp(segment.StartSeconds)).AppendLine()
                .Append("  speaker: ").Append(segment.Speaker).AppendLine()
                .Append("  text: ").Append(segment.Text).AppendLine();
        }

        return builder.ToString().Trim();
    }
}

internal sealed record TranscriptSummarySource(
    string SourceKind,
    IReadOnlyList<TranscriptSummaryChunk> Chunks);
