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
                TranscriptPolishingChunkResult chunkResult;
                try
                {
                    chunkResult = await runtime.PolishChunkAsync(options, chunk, cancellationToken);
                }
                catch (ReviewWorkerException exception) when (ShouldAttemptChunkModelFallback(options, exception))
                {
                    if (await TryPolishChunkWithModelFallbackAsync(
                        options,
                        TimeSpan.Zero,
                        options.ChunkFallbackOptions!,
                        chunk,
                        exception.Message,
                        cancellationToken) is not { } exceptionFallbackChunkResult)
                    {
                        exceptionFallbackChunkResult = BuildSourceFallbackChunkResult(
                            chunk,
                            TimeSpan.Zero,
                            exception.Message);
                    }

                    chunkResults.Add(exceptionFallbackChunkResult);
                    duration += exceptionFallbackChunkResult.Duration;
                    continue;
                }
                catch (TimeoutException exception) when (ShouldAttemptChunkModelFallback(options, exception))
                {
                    if (await TryPolishChunkWithModelFallbackAsync(
                        options,
                        TimeSpan.Zero,
                        options.ChunkFallbackOptions!,
                        chunk,
                        exception.Message,
                        cancellationToken) is not { } timeoutFallbackChunkResult)
                    {
                        timeoutFallbackChunkResult = BuildSourceFallbackChunkResult(
                            chunk,
                            TimeSpan.Zero,
                            exception.Message);
                    }

                    chunkResults.Add(timeoutFallbackChunkResult);
                    duration += timeoutFallbackChunkResult.Duration;
                    continue;
                }

                var normalizedContent = NormalizeAndValidateChunk(options, chunkResult, out var fallbackReason);
                if (fallbackReason.Length > 0 &&
                    options.ChunkFallbackOptions is not null &&
                    await TryPolishChunkWithModelFallbackAsync(
                        options,
                        chunkResult.Duration,
                        options.ChunkFallbackOptions,
                        chunk,
                        fallbackReason,
                        cancellationToken) is { } fallbackChunkResult)
                {
                    chunkResult = fallbackChunkResult;
                    normalizedContent = fallbackChunkResult.Content;
                    fallbackReason = string.Empty;
                }

                if (fallbackReason.Length > 0)
                {
                    if (!string.Equals(fallbackReason, TranscriptPolishingFallbackBuilder.MissingTimestampReason, StringComparison.Ordinal) ||
                        !TranscriptPolishingFallbackBuilder.TryRecoverMissingTimestampContent(chunk, normalizedContent, out var recoveredContent) ||
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
        finally
        {
            EndRuntimeSession(options);
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

    private void EndRuntimeSession(TranscriptPolishingOptions options)
    {
        if (runtime is not ITranscriptPolishingRuntimeSession runtimeSession)
        {
            return;
        }

        try
        {
            runtimeSession.EndPolishingSession(options);
        }
        catch (Exception)
        {
        }
    }

    private async Task<TranscriptPolishingChunkResult?> TryPolishChunkWithModelFallbackAsync(
        TranscriptPolishingOptions primaryOptions,
        TimeSpan primaryDuration,
        TranscriptPolishingOptions fallbackOptions,
        TranscriptPolishingChunk chunk,
        string primaryFailureReason,
        CancellationToken cancellationToken)
    {
        EndRuntimeSession(primaryOptions);

        TranscriptPolishingChunkResult fallbackResult;
        try
        {
            fallbackResult = await runtime.PolishChunkAsync(fallbackOptions, chunk, cancellationToken);
        }
        catch (Exception exception) when (exception is ReviewWorkerException or TimeoutException)
        {
            return null;
        }

        var normalizedContent = NormalizeAndValidateChunk(fallbackOptions, fallbackResult, out var fallbackReason);
        if (fallbackReason.Length > 0)
        {
            if (!string.Equals(fallbackReason, TranscriptPolishingFallbackBuilder.MissingTimestampReason, StringComparison.Ordinal) ||
                !TranscriptPolishingFallbackBuilder.TryRecoverMissingTimestampContent(chunk, normalizedContent, out var recoveredContent) ||
                !TranscriptPolishingFallbackBuilder.IsChunkOutputUsable(chunk, recoveredContent, out fallbackReason))
            {
                return null;
            }

            normalizedContent = recoveredContent;
        }

        return fallbackResult with
        {
            Content = normalizedContent,
            Duration = primaryDuration + fallbackResult.Duration,
            UsedFallback = true,
            FallbackReason = $"model={fallbackOptions.ModelId}; primary={primaryFailureReason}"
        };
    }

    private static TranscriptPolishingChunkResult BuildSourceFallbackChunkResult(
        TranscriptPolishingChunk chunk,
        TimeSpan primaryDuration,
        string primaryFailureReason)
    {
        return new TranscriptPolishingChunkResult(
            chunk,
            TranscriptPolishingFallbackBuilder.BuildFallbackChunkContent(chunk),
            primaryDuration,
            UsedFallback: true,
            FallbackReason: $"source_chunk; primary={primaryFailureReason}");
    }

    private static string NormalizeAndValidateChunk(
        TranscriptPolishingOptions options,
        TranscriptPolishingChunkResult chunkResult,
        out string fallbackReason)
    {
        var normalizedContent = TranscriptPolishingOutputNormalizer.Normalize(chunkResult.Content);
        if (ShouldUseStrictAnomalyDetection(options) &&
            TranscriptPolishingOutputAnomalyDetector.TryFindCriticalAnomaly(
                chunkResult.Content,
                normalizedContent,
                out fallbackReason))
        {
            return normalizedContent;
        }

        if (!TranscriptPolishingFallbackBuilder.IsChunkOutputUsable(chunkResult.Chunk, normalizedContent, out fallbackReason))
        {
            return normalizedContent;
        }

        fallbackReason = string.Empty;
        return normalizedContent;
    }

    private static bool ShouldUseStrictAnomalyDetection(TranscriptPolishingOptions options)
    {
        return KoeNote.App.Services.Llm.Gemma12BLocalValidation.IsTargetModel(options.ModelId) ||
            options.GenerationProfile.Contains("gemma12b", StringComparison.OrdinalIgnoreCase) ||
            options.PromptTemplateId.Contains("gemma12b", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldAttemptChunkModelFallback(
        TranscriptPolishingOptions options,
        ReviewWorkerException exception)
    {
        return options.ChunkFallbackOptions is not null &&
            exception.Category == ReviewFailureCategory.JsonParseFailed &&
            ShouldUseStrictAnomalyDetection(options);
    }

    private static bool ShouldAttemptChunkModelFallback(
        TranscriptPolishingOptions options,
        TimeoutException exception)
    {
        return options.ChunkFallbackOptions is not null &&
            ShouldUseStrictAnomalyDetection(options);
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
