using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Presets;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class TranscriptSummaryServiceTests
{
    [Fact]
    public async Task SummarizeAsync_GeneratesMarkdownSummaryDerivativeAndChunksFromRawTranscript()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "今日は仕様を確認します", "今日は仕様を確認します"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", "次回までに資料を作ります", "次回までに資料を作ります"),
            new TranscriptSegment("000003", "job-001", 2, 3, "Speaker_0", "期限は未定です", "期限は未定です")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakeSummaryRuntime());

        var result = await service.SummarizeAsync(CreateOptions("job-001", chunkSegmentCount: 2));

        Assert.Equal("job-001", result.JobId);
        Assert.Equal(TranscriptDerivativeSourceKinds.Raw, result.SourceKind);
        Assert.Equal(2, result.ChunkCount);
        Assert.Contains("## Overview", result.Content, StringComparison.Ordinal);

        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeKinds.Summary, derivative.Kind);
        Assert.Equal(TranscriptDerivativeFormats.Markdown, derivative.ContentFormat);
        Assert.Equal(TranscriptDerivativeSourceKinds.Raw, derivative.SourceKind);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Equal("000001..000003", derivative.SourceSegmentRange);
        Assert.Contains($"{result.DerivativeId}-chunk-001", derivative.SourceChunkIds, StringComparison.Ordinal);

        var chunks = derivativeRepository.ReadChunks(result.DerivativeId);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(["000001,000002", "000003"], chunks.Select(chunk => chunk.SourceSegmentIds).ToArray());
        Assert.All(chunks, chunk => Assert.Equal(TranscriptDerivativeSourceKinds.Raw, chunk.SourceKind));
        Assert.All(chunks, chunk => Assert.Equal(TranscriptDerivativeFormats.Markdown, chunk.ContentFormat));
    }

    [Fact]
    public async Task SummarizeAsync_UsesLatestNonStalePolishedDerivativeWhenAvailable()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "raw one", "raw one"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", "raw two", "raw two")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var sourceHash = derivativeRepository.ComputeCurrentRawTranscriptHash("job-001");
        var polished = derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "Polished one.\nPolished two.",
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            "000001..000002",
            null,
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight"));
        derivativeRepository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
            polished.DerivativeId,
            "job-001",
            1,
            TranscriptDerivativeSourceKinds.Raw,
            "000001,000002",
            0,
            2,
            sourceHash,
            TranscriptDerivativeFormats.PlainText,
            "Polished chunk text.",
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight",
            ChunkId: $"{polished.DerivativeId}-chunk-001"));
        var runtime = new FakeSummaryRuntime();
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            runtime);

        var result = await service.SummarizeAsync(CreateOptions("job-001"));

        Assert.Equal(TranscriptDerivativeSourceKinds.Polished, result.SourceKind);
        Assert.Single(runtime.SeenChunks);
        Assert.Equal(TranscriptDerivativeSourceKinds.Polished, runtime.SeenChunks[0].SourceKind);
        Assert.Contains("Polished chunk text.", runtime.SeenChunks[0].Content, StringComparison.Ordinal);

        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeSourceKinds.Polished, derivative.SourceKind);
        Assert.Equal(sourceHash, derivative.SourceTranscriptHash);
    }

    [Fact]
    public async Task SummarizeAsync_SkipsFinalMergeWhenSourceHasSingleChunk()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text", "text")
        ]);
        var runtime = new FakeSummaryRuntime(chunkSummary: """
            ## Overview

            Single chunk summary with enough detail.

            ## Key points

            - Key point from source.
            """);
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            new TranscriptDerivativeRepository(fixture.Paths),
            runtime);

        var result = await service.SummarizeAsync(CreateOptions("job-001"));

        Assert.Equal(1, result.ChunkCount);
        Assert.Single(runtime.SeenChunks);
        Assert.Equal(0, runtime.MergeCallCount);
        Assert.Contains("Single chunk summary", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_FallsBackToRawWhenLatestPolishedDerivativeIsStale()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "old raw", "old raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var oldHash = derivativeRepository.ComputeCurrentRawTranscriptHash("job-001");
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "Old polished.",
            TranscriptDerivativeSourceKinds.Raw,
            oldHash,
            "000001..000001",
            null,
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight"));
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "changed raw", "changed raw")
        ]);
        var runtime = new FakeSummaryRuntime();
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            runtime);

        var result = await service.SummarizeAsync(CreateOptions("job-001"));

        Assert.Equal(TranscriptDerivativeSourceKinds.Raw, result.SourceKind);
        Assert.Single(runtime.SeenChunks);
        Assert.Contains("changed raw", runtime.SeenChunks[0].Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SummarizeAsync_UsesFallbackWhenFinalSummaryIsEmpty()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text one", "text one"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", "text two", "text two")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakeSummaryRuntime(finalSummary: "   "));

        var result = await service.SummarizeAsync(CreateOptions("job-001", chunkSegmentCount: 1));

        Assert.Contains("## 概要", result.Content, StringComparison.Ordinal);
        Assert.Contains("簡易要約", result.Content, StringComparison.Ordinal);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeKinds.Summary, derivative.Kind);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Null(derivative.ErrorMessage);
        Assert.Equal("lightweight-fallback", derivative.GenerationProfile);
    }

    [Fact]
    public async Task SummarizeAsync_UsesFallbackWhenChunkSummaryIsEmpty()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text", "text")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakeSummaryRuntime(chunkSummary: "   "));

        var result = await service.SummarizeAsync(CreateOptions("job-001"));

        Assert.Contains("## 概要", result.Content, StringComparison.Ordinal);
        Assert.Contains("Transcript summary returned empty chunk output.", result.Content, StringComparison.Ordinal);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Null(derivative.ErrorMessage);
    }

    [Fact]
    public async Task SummarizeAsync_PreservesOriginalChunkMetadataWhenRuntimeReturnsAlteredChunk()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text", "text")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakeSummaryRuntime(alterReturnedChunk: true));

        var result = await service.SummarizeAsync(CreateOptions("job-001"));

        var chunks = derivativeRepository.ReadChunks(result.DerivativeId);
        var chunk = Assert.Single(chunks);
        Assert.Equal(1, chunk.ChunkIndex);
        Assert.Equal(TranscriptDerivativeSourceKinds.Raw, chunk.SourceKind);
        Assert.Equal("000001", chunk.SourceSegmentIds);
        Assert.Equal(0, chunk.SourceStartSeconds);
        Assert.Equal(1, chunk.SourceEndSeconds);
    }

    [Fact]
    public async Task SummarizeAsync_UsesFallbackWhenFinalSummaryIsUnexpectedlyShort()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", new string('a', 600), new string('a', 600)),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", new string('b', 600), new string('b', 600))
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakeSummaryRuntime(finalSummary: "too short"));

        var result = await service.SummarizeAsync(CreateOptions("job-001", chunkSegmentCount: 1));

        Assert.Contains("## 概要", result.Content, StringComparison.Ordinal);
        Assert.Contains("Transcript summary was unexpectedly short", result.Content, StringComparison.Ordinal);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Null(derivative.ErrorMessage);
    }

    [Fact]
    public async Task SummarizeAsync_RejectsMissingSegments()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var service = new TranscriptSummaryService(
            new TranscriptReadRepository(fixture.Paths),
            new TranscriptDerivativeRepository(fixture.Paths),
            new FakeSummaryRuntime());

        var exception = await Assert.ThrowsAsync<ReviewWorkerException>(() => service.SummarizeAsync(CreateOptions("missing-job")));

        Assert.Equal(ReviewFailureCategory.MissingSegments, exception.Category);
    }

    [Fact]
    public void PromptBuilder_IncludesRequiredSummarySafetyRules()
    {
        var prompt = new TranscriptSummaryPromptBuilder().BuildChunkPrompt(new TranscriptSummaryChunk(
            1,
            TranscriptDerivativeSourceKinds.Raw,
            "000001",
            0,
            1,
            "- segment_id: 000001\n  speaker: Speaker_0\n  text: 次回までに資料を作ります"));

        Assert.Contains("Do not add facts", prompt, StringComparison.Ordinal);
        Assert.Contains("Unspecified", prompt, StringComparison.Ordinal);
        Assert.Contains("## Overview", prompt, StringComparison.Ordinal);
        Assert.Contains("## Action items", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not output analysis", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not repeat or quote these instructions", prompt, StringComparison.Ordinal);
        Assert.Contains("If the source transcript is Japanese, write the summary in Japanese", prompt, StringComparison.Ordinal);
        Assert.Contains("Begin with the Overview section heading", prompt, StringComparison.Ordinal);
        Assert.Contains("Source segment ids: 000001", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_UsesCompactJapaneseNoThinkPromptForBonsai()
    {
        var prompt = new TranscriptSummaryPromptBuilder().BuildChunkPrompt(new TranscriptSummaryChunk(
            1,
            TranscriptDerivativeSourceKinds.Raw,
            "000001",
            0,
            1,
            "- segment_id: 000001\n  speaker: Speaker_0\n  text: 次回までに資料を作ります"),
            "bonsai-8b-q1-0");

        Assert.Contains("/no_think", prompt, StringComparison.Ordinal);
        Assert.Contains("日本語で短く要約", prompt, StringComparison.Ordinal);
        Assert.Contains("## 概要", prompt, StringComparison.Ordinal);
        Assert.Contains("## 主な内容", prompt, StringComparison.Ordinal);
        Assert.Contains("各セクションは最大3項目", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("## Overview", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_IncludesDomainDictionaryHintsForSummary()
    {
        var context = new DomainPromptContext(
            [new DomainPromptTerm("甲府盆地", "asr_hotword")],
            [new DomainPromptCorrectionPair("幸福盆地", "甲府盆地", "ASR誤認識", "global")],
            ["地名と観光表現は文脈に合う場合だけ正しい表記へ寄せる。"]);

        var prompt = new TranscriptSummaryPromptBuilder().BuildChunkPrompt(
            new TranscriptSummaryChunk(
                1,
                TranscriptDerivativeSourceKinds.Raw,
                "000001",
                0,
                1,
                "- segment_id: 000001\n  speaker: Speaker_0\n  text: 幸福盆地の桃農園です"),
            "gemma-4-e4b-it-q4-k-m",
            context);

        Assert.Contains("Domain dictionary hints:", prompt, StringComparison.Ordinal);
        Assert.Contains("甲府盆地", prompt, StringComparison.Ordinal);
        Assert.Contains("幸福盆地 -> 甲府盆地", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not add facts that are not in the source", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_IncludesDomainDictionaryHintsForFinalSummary()
    {
        var context = new DomainPromptContext(
            [new DomainPromptTerm("桃源郷", "asr_hotword")],
            [new DomainPromptCorrectionPair("桃源橋", "桃源郷", "ASR誤認識", "global")],
            []);

        var prompt = new TranscriptSummaryPromptBuilder().BuildFinalPrompt(
            [
                new TranscriptSummaryChunkResult(
                    new TranscriptSummaryChunk(1, TranscriptDerivativeSourceKinds.Raw, "000001", 0, 1, "桃源橋の説明"),
                    "## 概要\n\n- 桃源橋の説明です。",
                    TimeSpan.FromSeconds(1))
            ],
            "bonsai-8b-q1-0",
            context);

        Assert.Contains("辞書ヒント:", prompt, StringComparison.Ordinal);
        Assert.Contains("桃源橋 -> 桃源郷", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_IncludesShortJapaneseDictionaryHintsForBonsai()
    {
        var context = new DomainPromptContext(
            Enumerable.Range(1, 12).Select(index => new DomainPromptTerm($"用語{index}", "asr_hotword")).ToArray(),
            Enumerable.Range(1, 12).Select(index => new DomainPromptCorrectionPair($"誤り{index}", $"正解{index}", "ASR誤認識", "global")).ToArray(),
            Enumerable.Range(1, 6).Select(index => $"指針{index}").ToArray());

        var prompt = new TranscriptSummaryPromptBuilder().BuildChunkPrompt(
            new TranscriptSummaryChunk(
                1,
                TranscriptDerivativeSourceKinds.Raw,
                "000001",
                0,
                1,
                "- segment_id: 000001\n  speaker: Speaker_0\n  text: 誤り1の説明です"),
            "bonsai-8b-q1-0",
            context);

        Assert.Contains("辞書ヒント:", prompt, StringComparison.Ordinal);
        Assert.Contains("文脈に合う場合だけ使う", prompt, StringComparison.Ordinal);
        Assert.Contains("用語10", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("用語11", prompt, StringComparison.Ordinal);
        Assert.Contains("誤り10 -> 正解10", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("誤り11 -> 正解11", prompt, StringComparison.Ordinal);
        Assert.Contains("指針5", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("指針6", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_OmitsDomainDictionaryHintsWhenContextIsEmpty()
    {
        var prompt = new TranscriptSummaryPromptBuilder().BuildChunkPrompt(
            new TranscriptSummaryChunk(
                1,
                TranscriptDerivativeSourceKinds.Raw,
                "000001",
                0,
                1,
                "- segment_id: 000001\n  speaker: Speaker_0\n  text: 次回までに資料を作ります"),
            "bonsai-8b-q1-0",
            new DomainPromptContext([], [], []));

        Assert.DoesNotContain("辞書ヒント:", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Domain dictionary hints:", prompt, StringComparison.Ordinal);
    }

    private static TranscriptSummaryOptions CreateOptions(string jobId, int chunkSegmentCount = 80)
    {
        return new TranscriptSummaryOptions(
            jobId,
            "llama-completion.exe",
            "model.gguf",
            Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N")),
            "bonsai-8b-q1-0",
            "lightweight",
            ChunkSegmentCount: chunkSegmentCount);
    }

    private static void SaveSegments(AppPaths paths, IReadOnlyList<TranscriptSegment> segments)
    {
        new TranscriptSegmentRepository(paths).SaveSegments(segments);
    }

    private sealed class FakeSummaryRuntime(
        string? finalSummary = null,
        string? chunkSummary = null,
        bool alterReturnedChunk = false) : ITranscriptSummaryRuntime
    {
        public List<TranscriptSummaryChunk> SeenChunks { get; } = [];

        public int MergeCallCount { get; private set; }

        public Task<TranscriptSummaryChunkResult> SummarizeChunkAsync(
            TranscriptSummaryOptions options,
            TranscriptSummaryChunk chunk,
            CancellationToken cancellationToken = default)
        {
            SeenChunks.Add(chunk);
            var returnedChunk = alterReturnedChunk
                ? chunk with
                {
                    ChunkIndex = 99,
                    SourceKind = TranscriptDerivativeSourceKinds.Polished,
                    SourceSegmentIds = "altered",
                    SourceStartSeconds = 100,
                    SourceEndSeconds = 200
                }
                : chunk;

            return Task.FromResult(new TranscriptSummaryChunkResult(
                returnedChunk,
                chunkSummary ?? $"## Overview{Environment.NewLine}{Environment.NewLine}Chunk {chunk.ChunkIndex}: {chunk.Content}",
                TimeSpan.FromMilliseconds(10)));
        }

        public Task<string> MergeSummariesAsync(
            TranscriptSummaryOptions options,
            IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
            CancellationToken cancellationToken = default)
        {
            MergeCallCount++;
            return Task.FromResult(finalSummary ?? $$"""
                ## Overview

                Summary from {{chunkResults.Count}} chunk(s).

                ## Key points

                - Key point from source.

                ## Decisions

                - Unspecified.

                ## Action items

                - Unspecified.

                ## Open questions

                - Unspecified.

                ## Keywords

                - transcript
                """);
        }
    }
}
