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
                var normalizedContent = TranscriptPolishingOutputNormalizer.Normalize(chunkResult.Content);
                if (!IsChunkOutputUsable(chunk, normalizedContent, out var fallbackReason))
                {
                    if (!TryRecoverMissingTimestampContent(chunk, normalizedContent, out var recoveredContent) ||
                        !IsChunkOutputUsable(chunk, recoveredContent, out fallbackReason))
                    {
                        normalizedContent = BuildFallbackChunkContent(chunk);
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

    private static IEnumerable<IReadOnlyList<TranscriptReadModel>> BuildSpeakerBlocks(
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

    private static string BuildSegmentRange(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments.Count == 0 ? string.Empty : $"{segments[0].SegmentId}..{segments[^1].SegmentId}";
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

    private static bool IsChunkOutputUsable(TranscriptPolishingChunk chunk, string content, out string reason)
    {
        if (!TranscriptPolishingOutputNormalizer.IsUsableDocument(content, out reason))
        {
            return false;
        }

        var sourceLength = chunk.Segments.Sum(static segment => Math.Max(0, segment.Text.Length));
        if (sourceLength > 0 && content.Length > 2000 && content.Length > sourceLength * 6)
        {
            reason = "expanded far beyond the source chunk";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static string BuildFallbackChunkContent(TranscriptPolishingChunk chunk)
    {
        var blocks = new List<string>();
        foreach (var speakerBlock in BuildSpeakerBlocks(chunk.Segments))
        {
            var first = speakerBlock[0];
            var last = speakerBlock[^1];
            var text = string.Join(
                Environment.NewLine,
                speakerBlock
                    .Select(static segment => segment.Text.Trim())
                    .Where(static text => text.Length > 0));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            blocks.Add($"[{FormatTimestamp(first.StartSeconds)} - {FormatTimestamp(last.EndSeconds)}] {first.Speaker}: {text}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }

    private static bool TryRecoverMissingTimestampContent(
        TranscriptPolishingChunk chunk,
        string content,
        out string recoveredContent)
    {
        recoveredContent = string.Empty;
        if (string.IsNullOrWhiteSpace(content) || content.Contains('\uFFFD', StringComparison.Ordinal))
        {
            return false;
        }

        var sourceBlocks = BuildSpeakerBlocks(chunk.Segments).ToArray();
        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(static line => line.Trim())
            .Where(static line => line.Length > 0)
            .ToArray();
        if (sourceBlocks.Length == 0 || lines.Length != sourceBlocks.Length)
        {
            return false;
        }

        var recoveredBlocks = new List<string>(sourceBlocks.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            var sourceBlock = sourceBlocks[index];
            var first = sourceBlock[0];
            var last = sourceBlock[^1];
            var text = StripSpeakerPrefix(lines[index], first.Speaker).Trim();
            if (string.IsNullOrWhiteSpace(text) || LooksLikeGeneratedWrapper(text))
            {
                return false;
            }

            recoveredBlocks.Add($"[{FormatTimestamp(first.StartSeconds)} - {FormatTimestamp(last.EndSeconds)}] {first.Speaker}: {text}");
        }

        recoveredContent = string.Join(Environment.NewLine + Environment.NewLine, recoveredBlocks);
        return true;
    }

    private static string StripSpeakerPrefix(string line, string speaker)
    {
        var prefixes = new[]
        {
            $"{speaker}:",
            $"{speaker}："
        };

        foreach (var prefix in prefixes)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return line[prefix.Length..];
            }
        }

        return line;
    }

    private static bool LooksLikeGeneratedWrapper(string text)
    {
        return text.StartsWith("BEGIN_BLOCK", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("END_BLOCK", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("Output:", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
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
