using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptPolishingService(
    TranscriptReadRepository transcriptReadRepository,
    TranscriptDerivativeRepository derivativeRepository,
    ITranscriptPolishingRuntime runtime)
{
    public async Task<TranscriptPolishingResult> PolishAsync(
        TranscriptPolishingOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        var segments = transcriptReadRepository.ReadForJob(options.JobId);
        if (segments.Count == 0)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingSegments, "No transcript segments were available for polishing.");
        }

        var sourceHash = TranscriptDerivativeRepository.ComputeSourceTranscriptHash(segments);
        var chunks = BuildChunks(segments, options.ChunkSegmentCount).ToArray();
        var duration = TimeSpan.Zero;
        var chunkResults = new List<TranscriptPolishingChunkResult>(chunks.Length);

        try
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkResult = await runtime.PolishChunkAsync(options, chunk, cancellationToken);
                chunkResults.Add(chunkResult);
                duration += chunkResult.Duration;
            }
        }
        catch (ReviewWorkerException exception)
        {
            return SaveFailed(options, sourceHash, exception.Message, BuildSegmentRange(segments));
        }
        catch (TimeoutException exception)
        {
            return SaveFailed(options, sourceHash, exception.Message, BuildSegmentRange(segments));
        }

        var content = string.Join(Environment.NewLine + Environment.NewLine, chunkResults.Select(static result => result.Content.Trim()))
            .Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return SaveFailed(options, sourceHash, "Transcript polishing returned empty output.", BuildSegmentRange(segments));
        }

        var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            options.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            content,
            TranscriptDerivativeSourceKinds.Raw,
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
                TranscriptDerivativeSourceKinds.Raw,
                chunkResult.Chunk.SourceSegmentIds,
                chunkResult.Chunk.SourceStartSeconds,
                chunkResult.Chunk.SourceEndSeconds,
                sourceHash,
                TranscriptDerivativeFormats.PlainText,
                chunkResult.Content,
                options.ModelId,
                options.PromptVersion,
                options.GenerationProfile,
                ChunkId: BuildChunkId(derivative.DerivativeId, chunkResult.Chunk)));
        }

        var derivativeId = derivative.DerivativeId;
        derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            options.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            content,
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            BuildSegmentRange(segments),
            string.Join(",", chunkResults.Select(result => BuildChunkId(derivativeId, result.Chunk))),
            options.ModelId,
            options.PromptVersion,
            options.GenerationProfile,
            DerivativeId: derivativeId));

        return new TranscriptPolishingResult(
            options.JobId,
            derivative.DerivativeId,
            content,
            sourceHash,
            chunks.Length,
            duration);
    }

    private TranscriptPolishingResult SaveFailed(
        TranscriptPolishingOptions options,
        string sourceHash,
        string errorMessage,
        string? sourceSegmentRange)
    {
        var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            options.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            string.Empty,
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            sourceSegmentRange,
            null,
            options.ModelId,
            options.PromptVersion,
            options.GenerationProfile,
            TranscriptDerivativeStatuses.Failed,
            errorMessage));

        return new TranscriptPolishingResult(
            options.JobId,
            derivative.DerivativeId,
            string.Empty,
            sourceHash,
            0,
            TimeSpan.Zero);
    }

    private static IEnumerable<TranscriptPolishingChunk> BuildChunks(
        IReadOnlyList<TranscriptReadModel> segments,
        int chunkSegmentCount)
    {
        for (var index = 0; index < segments.Count; index += chunkSegmentCount)
        {
            yield return new TranscriptPolishingChunk(
                (index / chunkSegmentCount) + 1,
                segments.Skip(index).Take(chunkSegmentCount).ToArray());
        }
    }

    private static string BuildSegmentRange(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments.Count == 0 ? string.Empty : $"{segments[0].SegmentId}..{segments[^1].SegmentId}";
    }

    private static string BuildChunkId(string derivativeId, TranscriptPolishingChunk chunk)
    {
        return $"{derivativeId}-chunk-{chunk.ChunkIndex:D3}";
    }

    private static void ValidateOptions(TranscriptPolishingOptions options)
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
}
