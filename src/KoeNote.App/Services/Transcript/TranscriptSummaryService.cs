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
                var chunkResult = await SummarizeChunkWithRetryAsync(options, chunk, cancellationToken);
                var chunkValidation = ValidateSummaryContent(
                    chunkResult.Content,
                    options,
                    requireStructuredSections: false);
                if (!chunkValidation.IsValid)
                {
                    return SaveFallback(
                        options,
                        source,
                        sourceHash,
                        segments,
                        chunkValidation.Reason,
                        chunkResults);
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
                var mergeResult = await MergeSummariesWithRetryAsync(options, chunkResults, cancellationToken);
                content = mergeResult.Content.Trim();
                duration += mergeResult.Duration;
            }

            var finalValidation = ValidateSummaryContent(
                content,
                options,
                requireStructuredSections: true);
            if (!finalValidation.IsValid)
            {
                return SaveFallback(
                    options,
                    source,
                    sourceHash,
                    segments,
                    finalValidation.Reason,
                    chunkResults);
            }

            content = SummaryTextNormalizer.NormalizeUserFacingSummary(content);

            if (IsUnexpectedlyShort(content, source.Chunks))
            {
                return SaveFallback(
                    options,
                    source,
                    sourceHash,
                    segments,
                    "Transcript summary was unexpectedly short for the source transcript.",
                    chunkResults);
            }

            var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                options.JobId,
                TranscriptDerivativeKinds.Summary,
                TranscriptDerivativeFormats.Markdown,
                content,
                source.SourceKind,
                sourceHash,
                TranscriptSummaryFallbackBuilder.BuildSegmentRange(segments),
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
                    ChunkId: TranscriptSummaryFallbackBuilder.BuildChunkId(derivative.DerivativeId, chunkResult.Chunk)));
            }

            var derivativeId = derivative.DerivativeId;
            derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
                options.JobId,
                TranscriptDerivativeKinds.Summary,
                TranscriptDerivativeFormats.Markdown,
                content,
                source.SourceKind,
                sourceHash,
                TranscriptSummaryFallbackBuilder.BuildSegmentRange(segments),
                string.Join(",", chunkResults.Select(result => TranscriptSummaryFallbackBuilder.BuildChunkId(derivativeId, result.Chunk))),
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
            return SaveFallback(options, source, sourceHash, segments, exception.Message, chunkResults);
        }
        catch (TimeoutException exception)
        {
            return SaveFallback(options, source, sourceHash, segments, exception.Message, chunkResults);
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

    private TranscriptSummaryResult SaveFallback(
        TranscriptSummaryOptions options,
        SummarySource source,
        string sourceHash,
        IReadOnlyList<TranscriptReadModel> segments,
        string reason,
        IReadOnlyList<TranscriptSummaryChunkResult>? chunkResults = null)
    {
        var content = TranscriptSummaryFallbackBuilder.BuildSummary(
            segments,
            chunkResults);
        var derivative = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            options.JobId,
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            content,
            source.SourceKind,
            sourceHash,
            TranscriptSummaryFallbackBuilder.BuildSegmentRange(segments),
            null,
            options.ModelId,
            options.PromptVersion,
            $"{options.GenerationProfile}-fallback"));

        foreach (var chunk in source.Chunks)
        {
            derivativeRepository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
                derivative.DerivativeId,
                options.JobId,
                chunk.ChunkIndex,
                chunk.SourceKind,
                chunk.SourceSegmentIds,
                chunk.SourceStartSeconds,
                chunk.SourceEndSeconds,
                sourceHash,
                TranscriptDerivativeFormats.Markdown,
                TranscriptSummaryFallbackBuilder.BuildChunkSummary(chunk),
                options.ModelId,
                options.PromptVersion,
                $"{options.GenerationProfile}-fallback",
                ChunkId: TranscriptSummaryFallbackBuilder.BuildChunkId(derivative.DerivativeId, chunk)));
        }

        return new TranscriptSummaryResult(
            options.JobId,
            derivative.DerivativeId,
            content,
            source.SourceKind,
            sourceHash,
            source.Chunks.Count,
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

    private async Task<TranscriptSummaryChunkResult> SummarizeChunkWithRetryAsync(
        TranscriptSummaryOptions options,
        TranscriptSummaryChunk chunk,
        CancellationToken cancellationToken)
    {
        TranscriptSummaryChunkResult? lastResult = null;
        ReviewWorkerException? lastException = null;
        var maxAttempts = Math.Max(1, options.MaxAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                lastResult = await runtime.SummarizeChunkAsync(
                    options with { Attempt = attempt },
                    chunk,
                    cancellationToken);
                var validation = ValidateSummaryContent(
                    lastResult.Content,
                    options,
                    requireStructuredSections: false);
                if (validation.IsValid)
                {
                    return lastResult;
                }
            }
            catch (ReviewWorkerException exception)
            {
                lastException = exception;
            }
        }

        if (lastResult is not null)
        {
            return lastResult;
        }

        throw lastException ?? new ReviewWorkerException(
            ReviewFailureCategory.JsonParseFailed,
            "Transcript summary failed validation.");
    }

    private async Task<MergeSummaryResult> MergeSummariesWithRetryAsync(
        TranscriptSummaryOptions options,
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        CancellationToken cancellationToken)
    {
        var duration = TimeSpan.Zero;
        var content = string.Empty;
        ReviewWorkerException? lastException = null;
        var maxAttempts = Math.Max(1, options.MaxAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                var startedAt = DateTimeOffset.UtcNow;
                content = (await runtime.MergeSummariesAsync(
                    options with { Attempt = attempt },
                    chunkResults,
                    cancellationToken)).Trim();
                duration += DateTimeOffset.UtcNow - startedAt;
                var validation = ValidateSummaryContent(
                    content,
                    options,
                    requireStructuredSections: true);
                if (validation.IsValid)
                {
                    return new MergeSummaryResult(content, duration);
                }
            }
            catch (ReviewWorkerException exception)
            {
                lastException = exception;
            }
        }

        if (!string.IsNullOrWhiteSpace(content))
        {
            return new MergeSummaryResult(content, duration);
        }

        throw lastException ?? new ReviewWorkerException(
            ReviewFailureCategory.JsonParseFailed,
            "Final transcript summary failed validation.");
    }

    private static TranscriptSummaryValidationResult ValidateSummaryContent(
        string content,
        TranscriptSummaryOptions options,
        bool requireStructuredSections)
    {
        return TranscriptSummaryValidator.Validate(
            content,
            options.ValidationMode,
            requireStructuredSections);
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

    private static bool IsUnexpectedlyShort(
        string finalSummary,
        IReadOnlyList<TranscriptSummaryChunk> sourceChunks)
    {
        var sourceLength = sourceChunks.Sum(static chunk => chunk.Content.Length);
        return sourceLength >= 1000 && finalSummary.Trim().Length < 80;
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

    private sealed record MergeSummaryResult(string Content, TimeSpan Duration);
}
