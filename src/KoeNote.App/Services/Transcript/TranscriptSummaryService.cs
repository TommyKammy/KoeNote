using System.Text;
using System.Text.RegularExpressions;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed partial class TranscriptSummaryService(
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

            content = NormalizeUserFacingSummary(content);

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

        var segmentFallbackBullets = BuildSegmentFallbackBullets(segments, 8);
        var fallbackActions = BuildFallbackActionItems([], segmentFallbackBullets);
        var fallbackKeywords = BuildFallbackKeywords([], segmentFallbackBullets, segmentFallbackBullets);

        var builder = new StringBuilder();
        builder
            .AppendLine("## Overview")
            .AppendLine();
        AppendBullets(builder, segmentFallbackBullets.Take(4));
        builder
            .AppendLine()
            .AppendLine("## Key points")
            .AppendLine();
        AppendBullets(builder, segmentFallbackBullets);

        builder
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine()
            .AppendLine("- 明示された決定事項はありません。")
            .AppendLine()
            .AppendLine("## Action items")
            .AppendLine();
        AppendBullets(builder, fallbackActions);
        builder
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine()
            .AppendLine("- 明示された未解決事項はありません。")
            .AppendLine()
            .AppendLine("## Keywords")
            .AppendLine();
        AppendBullets(builder, fallbackKeywords);

        return builder.ToString().Trim();
    }

    private static string BuildStructuredChunkFallbackSummary(
        SummarySource source,
        IReadOnlyList<TranscriptReadModel> segments,
        string reason,
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults)
    {
        var overviews = ExtractSectionBullets(chunkResults, ["Overview", "概要"], 4);
        var keyPoints = ExtractSectionBullets(chunkResults, ["Key points", "主な内容"], 9);
        var decisions = ExtractSectionBullets(chunkResults, ["Decisions", "決定事項"], 4);
        var actions = ExtractSectionBullets(chunkResults, ["Action items", "アクション項目"], 6);
        var openQuestions = ExtractSectionBullets(chunkResults, ["Open questions", "未解決の質問"], 4);
        var segmentFallbackBullets = BuildSegmentFallbackBullets(segments, 8);
        var keywords = NormalizeKeywordBullets(
            ExtractSectionBullets(chunkResults, ["Keywords", "キーワード"], 10),
            overviews.Concat(keyPoints).Concat(segmentFallbackBullets));
        var fallbackActions = actions.Length > 0
            ? actions
            : BuildFallbackActionItems(keyPoints, segmentFallbackBullets);

        var builder = new StringBuilder();
        builder
            .AppendLine("## Overview")
            .AppendLine();

        AppendBullets(builder, overviews.Length > 0
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

        AppendBullets(builder, keyPoints);
        builder
            .AppendLine()
            .AppendLine("## Decisions")
            .AppendLine();
        AppendBullets(builder, decisions.Length > 0 ? decisions : ["明示された決定事項はありません。"]);

        builder
            .AppendLine()
            .AppendLine("## Action items")
            .AppendLine();
        AppendBullets(builder, fallbackActions);

        builder
            .AppendLine()
            .AppendLine("## Open questions")
            .AppendLine();
        AppendBullets(builder, openQuestions.Length > 0 ? openQuestions : ["明示された未解決事項はありません。"]);

        builder
            .AppendLine()
            .AppendLine("## Keywords")
            .AppendLine();
        AppendBullets(builder, keywords.Length > 0
            ? keywords
            : BuildFallbackKeywords(overviews, keyPoints, segmentFallbackBullets));

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

    private static string[] BuildFallbackKeywords(
        IReadOnlyList<string> overviews,
        IReadOnlyList<string> keyPoints,
        IReadOnlyList<string> segmentFallbackBullets)
    {
        var sourceSentences = overviews
            .Concat(keyPoints)
            .Concat(segmentFallbackBullets)
            .Select(static sentence => NormalizeKeywordComparisonKey(sentence))
            .Where(static sentence => sentence.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return overviews
            .Concat(keyPoints)
            .Concat(segmentFallbackBullets)
            .SelectMany(SplitKeywordCandidates)
            .Select(NormalizeKeywordCandidate)
            .Where(keyword => IsUsefulKeyword(keyword, sourceSentences))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .DefaultIfEmpty("summary")
            .ToArray();
    }

    private static string[] NormalizeKeywordBullets(
        IReadOnlyList<string> keywordBullets,
        IEnumerable<string> sourceSentences)
    {
        var sourceSentenceKeys = sourceSentences
            .Select(static sentence => NormalizeKeywordComparisonKey(sentence))
            .Where(static sentence => sentence.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        return keywordBullets
            .SelectMany(SplitKeywords)
            .Select(static keyword => StripSourceReferences(keyword).Trim().TrimEnd('\u3002', '.', ','))
            .Where(keyword => IsUsefulKeyword(keyword, sourceSentenceKeys))
            .Distinct(StringComparer.Ordinal)
            .Take(10)
            .ToArray();
    }

    private static string[] BuildSegmentFallbackBullets(
        IReadOnlyList<TranscriptReadModel> segments,
        int limit)
    {
        return segments
            .Where(static segment => !string.IsNullOrWhiteSpace(segment.Text))
            .Select(static segment => TrimForSummary(segment.Text, 140))
            .Take(limit)
            .ToArray();
    }

    private static void AppendBullets(StringBuilder builder, IEnumerable<string> bullets)
    {
        foreach (var bullet in DeduplicateBullets(bullets))
        {
            builder
                .Append("- ")
                .AppendLine(bullet);
        }
    }

    private static IEnumerable<string> DeduplicateBullets(IEnumerable<string> bullets)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var bullet in bullets)
        {
            var normalized = TrimForSummary(StripSourceReferences(bullet), 220).Trim();
            if (normalized.Length == 0)
            {
                continue;
            }

            var key = normalized.Replace("\u3002", string.Empty, StringComparison.Ordinal);
            if (seen.Add(key))
            {
                yield return normalized;
            }
        }
    }

    private static string[] ExtractSectionBullets(
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        IReadOnlyCollection<string> sectionNames,
        int limit)
    {
        var bullets = new List<string>();
        foreach (var chunk in chunkResults)
        {
            var inSection = false;
            foreach (var rawLine in chunk.Content.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.StartsWith("## ", StringComparison.Ordinal))
                {
                    var heading = line[3..].Trim();
                    inSection = sectionNames.Any(sectionName => heading.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
                    continue;
                }

                if (!inSection || (!line.StartsWith("- ", StringComparison.Ordinal) && !line.StartsWith("* ", StringComparison.Ordinal)))
                {
                    continue;
                }

                var bullet = line[2..].Trim();
                if (bullet.Length == 0 || bullet.Equals("Unspecified.", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                bullets.Add(TrimForSummary(bullet, 220));
                if (bullets.Count >= limit)
                {
                    return bullets.ToArray();
                }
            }
        }

        return bullets.ToArray();
    }

    private static string NormalizeUserFacingSummary(string content)
    {
        var normalizedLines = new List<string>();
        var currentSection = string.Empty;
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();
            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                currentSection = trimmed[3..].Trim();
                normalizedLines.Add(line);
                continue;
            }

            if (trimmed.Length == 0)
            {
                normalizedLines.Add(line);
                continue;
            }

            if (IsUnspecifiedSection(currentSection) && IsUnspecifiedLine(trimmed))
            {
                normalizedLines.Add("- Unspecified.");
                continue;
            }

            if (IsKeywordSection(currentSection))
            {
                AppendKeywordLines(normalizedLines, trimmed);
                continue;
            }

            normalizedLines.Add(NormalizeUserFacingSummaryLine(line));
        }

        return string.Join(Environment.NewLine, normalizedLines).Trim();
    }

    private static string NormalizeUserFacingSummaryLine(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith("- ", StringComparison.Ordinal) &&
            !trimmed.StartsWith("* ", StringComparison.Ordinal))
        {
            return line;
        }

        var indentLength = line.Length - trimmed.Length;
        var prefix = line[..indentLength];
        return prefix + trimmed[..2] + StripSourceReferences(trimmed[2..]);
    }

    private static bool IsUnspecifiedSection(string section)
    {
        return section.Equals("Decisions", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("Action items", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("Open questions", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKeywordSection(string section)
    {
        return section.Equals("Keywords", StringComparison.OrdinalIgnoreCase) ||
            section.Equals("キーワード", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnspecifiedLine(string line)
    {
        return line.Trim().TrimEnd('.').Equals("Unspecified", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendKeywordLines(List<string> lines, string line)
    {
        var keywordSource = line.StartsWith("- ", StringComparison.Ordinal) || line.StartsWith("* ", StringComparison.Ordinal)
            ? line[2..]
            : line;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var keyword in SplitKeywords(keywordSource))
        {
            var normalized = StripSourceReferences(keyword).Trim().TrimEnd('\u3002', '.', ',');
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            lines.Add("- " + normalized);
            if (seen.Count >= 12)
            {
                return;
            }
        }
    }

    private static IEnumerable<string> SplitKeywords(string text)
    {
        return (text ?? string.Empty)
            .Split([',', '\u3001'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IEnumerable<string> SplitKeywordCandidates(string text)
    {
        return (text ?? string.Empty)
            .Replace(":", " ", StringComparison.Ordinal)
            .Replace("\uFF1A", " ", StringComparison.Ordinal)
            .Split([' ', '\t', ',', '.', '\u3001', '\u3002', '\u30fb', '/', '\u3000'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(SplitJapaneseKeywordToken)
            .Where(static token => !token.Equals("Unspecified", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SplitJapaneseKeywordToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            yield break;
        }

        yield return token;

        foreach (var part in JapaneseKeywordParticleRegex()
            .Split(token)
            .Select(NormalizeKeywordCandidate)
            .Where(static part => part.Length >= 2))
        {
            yield return part;
        }
    }

    private static string NormalizeKeywordCandidate(string keyword)
    {
        var normalized = StripSourceReferences(keyword).Trim().TrimEnd('\u3002', '.', ',');
        foreach (var prefix in new[] { "この", "その", "あの" })
        {
            if (normalized.StartsWith(prefix, StringComparison.Ordinal) && normalized.Length > prefix.Length + 1)
            {
                normalized = normalized[prefix.Length..];
                break;
            }
        }

        return normalized;
    }

    private static bool IsUsefulKeyword(string keyword, IReadOnlySet<string> sourceSentenceKeys)
    {
        var normalized = NormalizeKeywordCandidate(keyword);
        if (normalized.Length < 2)
        {
            return false;
        }

        if (sourceSentenceKeys.Contains(NormalizeKeywordComparisonKey(normalized)))
        {
            return false;
        }

        return normalized.Length <= 28 &&
            CountJapaneseParticles(normalized) <= 1 &&
            !normalized.Contains("です", StringComparison.Ordinal) &&
            !normalized.Contains("ます", StringComparison.Ordinal);
    }

    private static string NormalizeKeywordComparisonKey(string text)
    {
        return StripSourceReferences(text)
            .Trim()
            .TrimStart('-', '*')
            .Trim()
            .TrimEnd('\u3002', '.', ',');
    }

    private static int CountJapaneseParticles(string text)
    {
        string[] particles = ["は", "が", "を", "に", "で", "と", "も", "へ", "から", "まで", "より"];
        return particles.Count(particle => text.Contains(particle, StringComparison.Ordinal));
    }

    private static string StripSourceReferences(string text)
    {
        var withoutSegmentRefs = SourceReferenceRegex().Replace(text ?? string.Empty, string.Empty);
        var withoutEmphasis = BoldMarkdownRegex().Replace(withoutSegmentRefs, "$1");
        return string.Join(" ", withoutEmphasis.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
    }

    [GeneratedRegex(@"\s*(?:[\(\uFF08]\s*\[?\s*(?:segment_id:\s*)?\d+(?:\s*(?:,|-|\u2011|\u2013|\u2014|\u3001|\uFF5E|~)\s*\d+)*\s*\]?\s*[\)\uFF09]|\u3010\s*\d+(?:\s*(?:,|-|\u2011|\u2013|\u2014|\u3001|\uFF5E|~)\s*\d+)*\s*\u3011|\[\s*\d+(?:\s*(?:,|-|\u2011|\u2013|\u2014|\u3001|\uFF5E|~)\s*\d+)*\s*\])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceReferenceRegex();

    [GeneratedRegex(@"\*\*([^*]+)\*\*", RegexOptions.CultureInvariant)]
    private static partial Regex BoldMarkdownRegex();

    [GeneratedRegex("(?:には|では|から|まで|より|という|って|は|が|を|に|で|と|も|へ|の)", RegexOptions.CultureInvariant)]
    private static partial Regex JapaneseKeywordParticleRegex();

    private static string BuildFallbackChunkSummary(TranscriptSummaryChunk chunk)
    {
        return $"""
            ## 讎りｦ・

            LLM隕∫ｴ・′蛻ｩ逕ｨ縺ｧ縺阪↑縺九▲縺溘◆繧√√％縺ｮ繝√Ε繝ｳ繧ｯ縺ｯ譁・ｭ苓ｵｷ縺薙＠謚懃ｲ九→縺励※菫晏ｭ倥＠縺ｾ縺励◆縲・

            ## 荳ｻ縺ｪ蜀・ｮｹ

            {TrimForSummary(chunk.Content, 1000)}
            """;
    }

    private static string TrimForSummary(string text, int maxLength)
    {
        var normalized = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "...";
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
