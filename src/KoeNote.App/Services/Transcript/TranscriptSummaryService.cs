using System.Text;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptSummaryService(
    TranscriptReadRepository transcriptReadRepository,
    TranscriptDerivativeRepository derivativeRepository,
    ITranscriptSummaryRuntime runtime)
{
    public async Task<TranscriptSummaryResult> SummarizeAsync(
        TranscriptSummaryOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        var segments = transcriptReadRepository.ReadForJob(options.JobId);
        if (segments.Count == 0)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingSegments, "No transcript segments were available for summary.");
        }

        var sourceHash = TranscriptDerivativeRepository.ComputeSourceTranscriptHash(segments);
        var source = ResolveSource(options.JobId, segments, options.ChunkSegmentCount);
        var duration = TimeSpan.Zero;
        var chunkResults = new List<TranscriptSummaryChunkResult>(source.Chunks.Count);

        try
        {
            foreach (var chunk in source.Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkResult = await runtime.SummarizeChunkAsync(options, chunk, cancellationToken);
                if (string.IsNullOrWhiteSpace(chunkResult.Content))
                {
                    return SaveFailed(options, source.SourceKind, sourceHash, "Transcript summary returned empty chunk output.", BuildSegmentRange(segments));
                }

                chunkResults.Add(chunkResult with { Chunk = chunk });
                duration += chunkResult.Duration;
            }

            var content = string.Empty;
            if (chunkResults.Count == 1)
            {
                content = chunkResults[0].Content.Trim();
            }
            else
            {
                var finalStart = DateTimeOffset.UtcNow;
                content = (await runtime.MergeSummariesAsync(options, chunkResults, cancellationToken)).Trim();
                duration += DateTimeOffset.UtcNow - finalStart;
            }

            if (string.IsNullOrWhiteSpace(content))
            {
                return SaveFailed(options, source.SourceKind, sourceHash, "Transcript summary returned empty output.", BuildSegmentRange(segments));
            }

            if (IsUnexpectedlyShort(content, source.Chunks))
            {
                return SaveFailed(options, source.SourceKind, sourceHash, "Transcript summary was unexpectedly short for the source transcript.", BuildSegmentRange(segments));
            }

            var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                options.JobId,
                TranscriptDerivativeKinds.Summary,
                TranscriptDerivativeFormats.Markdown,
                content,
                source.SourceKind,
                sourceHash,
                BuildSegmentRange(segments),
                null,
                options.ModelId,
                options.PromptVersion,
                options.GenerationProfile));

            foreach (var chunkResult in chunkResults)
            {
                derivativeRepository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
                    derivative.DerivativeId,
                    options.JobId,
                    chunkResult.Chunk.ChunkIndex,
                    chunkResult.Chunk.SourceKind,
                    chunkResult.Chunk.SourceSegmentIds,
                    chunkResult.Chunk.SourceStartSeconds,
                    chunkResult.Chunk.SourceEndSeconds,
                    sourceHash,
                    TranscriptDerivativeFormats.Markdown,
                    chunkResult.Content,
                    options.ModelId,
                    options.PromptVersion,
                    options.GenerationProfile,
                    ChunkId: BuildChunkId(derivative.DerivativeId, chunkResult.Chunk)));
            }

            var derivativeId = derivative.DerivativeId;
            derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                options.JobId,
                TranscriptDerivativeKinds.Summary,
                TranscriptDerivativeFormats.Markdown,
                content,
                source.SourceKind,
                sourceHash,
                BuildSegmentRange(segments),
                string.Join(",", chunkResults.Select(result => BuildChunkId(derivativeId, result.Chunk))),
                options.ModelId,
                options.PromptVersion,
                options.GenerationProfile,
                DerivativeId: derivativeId));

            return new TranscriptSummaryResult(
                options.JobId,
                derivative.DerivativeId,
                content,
                source.SourceKind,
                sourceHash,
                source.Chunks.Count,
                duration);
        }
        catch (ReviewWorkerException exception)
        {
            return SaveFailed(options, source.SourceKind, sourceHash, exception.Message, BuildSegmentRange(segments));
        }
        catch (TimeoutException exception)
        {
            return SaveFailed(options, source.SourceKind, sourceHash, exception.Message, BuildSegmentRange(segments));
        }
    }

    private SummarySource ResolveSource(
        string jobId,
        IReadOnlyList<TranscriptReadModel> segments,
        int chunkSegmentCount)
    {
        var polished = derivativeRepository.ReadLatestSuccessful(jobId, TranscriptDerivativeKinds.Polished);
        if (polished is not null && !derivativeRepository.IsStale(polished))
        {
            var polishedChunks = derivativeRepository.ReadChunks(polished.DerivativeId)
                .Where(static chunk => string.Equals(chunk.Status, TranscriptDerivativeStatuses.Succeeded, StringComparison.Ordinal))
                .OrderBy(static chunk => chunk.ChunkIndex)
                .ToArray();

            if (polishedChunks.Length > 0)
            {
                return new SummarySource(
                    TranscriptDerivativeSourceKinds.Polished,
                    polishedChunks.Select(static chunk => new TranscriptSummaryChunk(
                        chunk.ChunkIndex,
                        TranscriptDerivativeSourceKinds.Polished,
                        chunk.SourceSegmentIds,
                        chunk.SourceStartSeconds,
                        chunk.SourceEndSeconds,
                        chunk.Content)).ToArray());
            }

            return new SummarySource(
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

        return new SummarySource(
            TranscriptDerivativeSourceKinds.Raw,
            BuildRawChunks(segments, chunkSegmentCount).ToArray());
    }

    private TranscriptSummaryResult SaveFailed(
        TranscriptSummaryOptions options,
        string sourceKind,
        string sourceHash,
        string errorMessage,
        string? sourceSegmentRange)
    {
        var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            options.JobId,
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            string.Empty,
            sourceKind,
            sourceHash,
            sourceSegmentRange,
            null,
            options.ModelId,
            options.PromptVersion,
            options.GenerationProfile,
            TranscriptDerivativeStatuses.Failed,
            errorMessage));

        return new TranscriptSummaryResult(
            options.JobId,
            derivative.DerivativeId,
            string.Empty,
            sourceKind,
            sourceHash,
            0,
            TimeSpan.Zero);
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
                .Append("  timestamp: ").Append(FormatTimestamp(segment.StartSeconds)).AppendLine()
                .Append("  speaker: ").Append(segment.Speaker).AppendLine()
                .Append("  text: ").Append(segment.Text).AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static bool IsUnexpectedlyShort(
        string finalSummary,
        IReadOnlyList<TranscriptSummaryChunk> sourceChunks)
    {
        var sourceLength = sourceChunks.Sum(static chunk => chunk.Content.Length);
        return sourceLength >= 1000 && finalSummary.Trim().Length < 80;
    }

    private static string BuildSegmentRange(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments.Count == 0 ? string.Empty : $"{segments[0].SegmentId}..{segments[^1].SegmentId}";
    }

    private static string BuildChunkId(string derivativeId, TranscriptSummaryChunk chunk)
    {
        return $"{derivativeId}-chunk-{chunk.ChunkIndex:D3}";
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private static void ValidateOptions(TranscriptSummaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.JobId))
        {
            throw new ArgumentException("Job id is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new ArgumentException("Model id is required.", nameof(options));
        }

        if (options.ChunkSegmentCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk segment count must be greater than zero.");
        }
    }

    private sealed record SummarySource(
        string SourceKind,
        IReadOnlyList<TranscriptSummaryChunk> Chunks);
}
