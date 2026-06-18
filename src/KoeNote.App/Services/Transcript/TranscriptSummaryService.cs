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
        var content = BuildFallbackSummary(source, segments, reason, chunkResults);
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
                BuildFallbackChunkSummary(chunk),
                options.ModelId,
                options.PromptVersion,
                $"{options.GenerationProfile}-fallback",
                ChunkId: BuildChunkId(derivative.DerivativeId, chunk)));
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
                .Append("  timestamp: ").Append(FormatTimestamp(segment.StartSeconds)).AppendLine()
                .Append("  speaker: ").Append(segment.Speaker).AppendLine()
                .Append("  text: ").Append(segment.Text).AppendLine();
        }

        return builder.ToString().Trim();
    }

    private static string BuildFallbackSummary(
        SummarySource source,
        IReadOnlyList<TranscriptReadModel> segments,
        string reason,
        IReadOnlyList<TranscriptSummaryChunkResult>? chunkResults = null)
    {
        var usableChunkResults = chunkResults?
            .Where(static result => !string.IsNullOrWhiteSpace(result.Content))
            .OrderBy(static result => result.Chunk.ChunkIndex)
            .ToArray();
        if (usableChunkResults is { Length: > 0 })
        {
            return BuildStructuredChunkFallbackSummary(source, segments, reason, usableChunkResults);
        }

        var segmentFallbackBullets = SummaryBulletParser.BuildSegmentFallbackBullets(segments, 8);
        var fallbackActions = BuildFallbackActionItems([], segmentFallbackBullets);
        var fallbackKeywords = SummaryKeywordExtractor.BuildFallbackKeywords([], segmentFallbackBullets, segmentFallbackBullets);

        var builder = new StringBuilder();
        builder
            .AppendLine("## Overview")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, segmentFallbackBullets.Take(4));
        builder
            .AppendLine()
            .AppendLine("## Key points")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, segmentFallbackBullets);

        builder
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine()
            .AppendLine("- 明示された決定事項はありません。")
            .AppendLine()
            .AppendLine("## Action items")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, fallbackActions);
        builder
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine()
            .AppendLine("- 明示された未解決事項はありません。")
            .AppendLine()
            .AppendLine("## Keywords")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, fallbackKeywords);

        return builder.ToString().Trim();
    }

    private static string BuildStructuredChunkFallbackSummary(
        SummarySource source,
        IReadOnlyList<TranscriptReadModel> segments,
        string reason,
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults)
    {
        var overviews = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Overview", "概要"], 4);
        var keyPoints = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Key points", "主な内容"], 9);
        var decisions = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Decisions", "決定事項"], 4);
        var actions = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Action items", "アクション項目"], 6);
        var openQuestions = SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Open questions", "未解決の質問"], 4);
        var segmentFallbackBullets = SummaryBulletParser.BuildSegmentFallbackBullets(segments, 8);
        var keywords = SummaryKeywordExtractor.NormalizeKeywordBullets(
            SummaryBulletParser.ExtractSectionBullets(chunkResults, ["Keywords", "キーワード"], 10),
            overviews.Concat(keyPoints).Concat(segmentFallbackBullets));
        var fallbackActions = actions.Length > 0
            ? actions
            : BuildFallbackActionItems(keyPoints, segmentFallbackBullets);

        var builder = new StringBuilder();
        builder
            .AppendLine("## Overview")
            .AppendLine();

        SummaryBulletParser.AppendBullets(builder, overviews.Length > 0
            ? overviews
            : keyPoints.Length > 0
                ? keyPoints.Take(4)
                : segmentFallbackBullets.Take(4));
        builder
            .AppendLine()
            .AppendLine("## Key points")
            .AppendLine();

        if (keyPoints.Length == 0)
        {
            keyPoints = segmentFallbackBullets;
        }

        SummaryBulletParser.AppendBullets(builder, keyPoints);
        builder
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, decisions.Length > 0 ? decisions : ["明示された決定事項はありません。"]);

        builder
            .AppendLine()
            .AppendLine("## Action items")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, fallbackActions);

        builder
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, openQuestions.Length > 0 ? openQuestions : ["明示された未解決事項はありません。"]);

        builder
            .AppendLine()
            .AppendLine("## Keywords")
            .AppendLine();
        SummaryBulletParser.AppendBullets(builder, keywords.Length > 0
            ? keywords
            : SummaryKeywordExtractor.BuildFallbackKeywords(overviews, keyPoints, segmentFallbackBullets));

        return builder.ToString().Trim();
    }

    private static string[] BuildFallbackActionItems(
        IReadOnlyList<string> keyPoints,
        IReadOnlyList<string> segmentFallbackBullets)
    {
        var actions = keyPoints
            .Concat(segmentFallbackBullets)
            .Where(ContainsActionCue)
            .Take(6)
            .ToArray();
        return actions.Length > 0 ? actions : ["明示されたアクション項目はありません。"];
    }

    private static bool ContainsActionCue(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] cues =
        [
            "必要",
            "推奨",
            "活用",
            "意識",
            "調べ",
            "確認",
            "準備",
            "取り組",
            "話し合",
            "確保",
            "使う",
            "作る",
            "plan",
            "prepare",
            "confirm",
            "review",
            "use",
            "follow"
        ];
        return cues.Any(cue => text.Contains(cue, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildFallbackChunkSummary(TranscriptSummaryChunk chunk)
    {
        return $"""
            ## 概要

            LLM 要約を利用できなかったため、このチャンクは文字起こしの抜粋として保存しました。

            ## 主な内容

            {SummaryTextNormalizer.TrimForSummary(chunk.Content, 1000)}
            """;
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

    private sealed record MergeSummaryResult(string Content, TimeSpan Duration);
}
