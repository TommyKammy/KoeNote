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
        var sourceSegmentRange = TranscriptPolishingChunkBuilder.BuildSegmentRange(segments);
        var chunks = TranscriptPolishingChunkBuilder.BuildChunks(segments, options.ChunkSegmentCount).ToArray();
        var duration = TimeSpan.Zero;
        var chunkResults = new List<TranscriptPolishingChunkResult>(chunks.Length);

        try
        {
            foreach (var chunk in chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkResult = await runtime.PolishChunkAsync(options, chunk, cancellationToken);
                var normalizedContent = TranscriptPolishingOutputNormalizer.Normalize(chunkResult.Content);
                if (!TranscriptPolishingFallbackBuilder.IsChunkOutputUsable(chunk, normalizedContent, out var fallbackReason))
                {
                    if (!TranscriptPolishingFallbackBuilder.TryRecoverMissingTimestampContent(chunk, normalizedContent, out var recoveredContent) ||
                        !TranscriptPolishingFallbackBuilder.IsChunkOutputUsable(chunk, recoveredContent, out fallbackReason))
                    {
                        normalizedContent = TranscriptPolishingFallbackBuilder.BuildFallbackChunkContent(chunk);
                        chunkResult = chunkResult with
                        {
                            UsedFallback = true,
                            FallbackReason = fallbackReason
                        };
                    }
                    else
                    {
                        normalizedContent = recoveredContent;
                    }
                }

                chunkResults.Add(chunkResult with { Content = normalizedContent });
                duration += chunkResult.Duration;
            }
        }
        catch (ReviewWorkerException exception)
        {
            return SaveFailed(options, sourceHash, exception.Message, sourceSegmentRange);
        }
        catch (TimeoutException exception)
        {
            return SaveFailed(options, sourceHash, exception.Message, sourceSegmentRange);
        }

        var content = string.Join(Environment.NewLine + Environment.NewLine, chunkResults.Select(static result => result.Content.Trim()))
            .Trim();
        if (string.IsNullOrWhiteSpace(content))
        {
            return SaveFailed(options, sourceHash, "Transcript polishing returned empty output.", sourceSegmentRange);
        }

        var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            options.JobId,
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            content,
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            sourceSegmentRange,
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
                BuildChunkGenerationProfile(options.GenerationProfile, chunkResult),
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
            sourceSegmentRange,
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

    private static string BuildChunkId(string derivativeId, TranscriptPolishingChunk chunk)
    {
        return $"{derivativeId}-chunk-{chunk.ChunkIndex:D3}";
    }

    private static string BuildChunkGenerationProfile(
        string generationProfile,
        TranscriptPolishingChunkResult chunkResult)
    {
        return chunkResult.UsedFallback
            ? $"{generationProfile}; fallback={chunkResult.FallbackReason}"
            : generationProfile;
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
