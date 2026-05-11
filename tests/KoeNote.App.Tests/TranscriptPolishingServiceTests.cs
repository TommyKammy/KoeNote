using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class TranscriptPolishingServiceTests
{
    [Fact]
    public async Task PolishAsync_GeneratesPolishedDerivativeAndChunks()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "えー今日はテストです", "えー今日はテストです"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_1", "はいよろしくお願いします", "はいよろしくお願いします"),
            new TranscriptSegment("000003", "job-001", 2, 3, "Speaker_0", "次に進みます", "次に進みます")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var runtime = new FakePolishingRuntime(chunk =>
            string.Join(Environment.NewLine + Environment.NewLine, chunk.Segments
                .GroupBy(static segment => segment.Speaker)
                .Select(group =>
                    $"[{FormatTestTimestamp(group.First().StartSeconds)} - {FormatTestTimestamp(group.Last().EndSeconds)}] {group.Key}: [{chunk.ChunkIndex}] {string.Join(" / ", group.Select(segment => segment.Text))}")));
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            runtime);

        var result = await service.PolishAsync(CreateOptions("job-001", chunkSegmentCount: 2));

        Assert.Equal("job-001", result.JobId);
        Assert.Equal(2, result.ChunkCount);
        Assert.Contains("[1]", result.Content, StringComparison.Ordinal);
        Assert.Contains("[2]", result.Content, StringComparison.Ordinal);

        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeKinds.Polished, derivative.Kind);
        Assert.Equal(TranscriptDerivativeSourceKinds.Raw, derivative.SourceKind);
        Assert.Equal(TranscriptDerivativeFormats.PlainText, derivative.ContentFormat);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Equal("000001..000003", derivative.SourceSegmentRange);
        Assert.Contains($"{result.DerivativeId}-chunk-001", derivative.SourceChunkIds, StringComparison.Ordinal);

        var chunks = derivativeRepository.ReadChunks(result.DerivativeId);
        Assert.Equal(2, chunks.Count);
        Assert.Equal(["000001,000002", "000003"], chunks.Select(chunk => chunk.SourceSegmentIds).ToArray());
        Assert.All(chunks, chunk => Assert.Equal(result.SourceTranscriptHash, chunk.SourceTranscriptHash));
    }

    [Fact]
    public async Task PolishAsync_KeepsConsecutiveSpeakerBlocksTogetherWhenChunking()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "first", "first"),
            new TranscriptSegment("000002", "job-001", 1, 2, "Speaker_0", "second", "second"),
            new TranscriptSegment("000003", "job-001", 2, 3, "Speaker_1", "third", "third"),
            new TranscriptSegment("000004", "job-001", 3, 4, "Speaker_1", "fourth", "fourth")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(chunk =>
                $"[{FormatTestTimestamp(chunk.Segments[0].StartSeconds)} - {FormatTestTimestamp(chunk.Segments[^1].EndSeconds)}] {chunk.Segments[0].Speaker}: {string.Join(",", chunk.Segments.Select(static segment => segment.SegmentId))}"));

        var result = await service.PolishAsync(CreateOptions("job-001", chunkSegmentCount: 3));

        Assert.Equal(2, result.ChunkCount);
        Assert.Contains("000001,000002", result.Content, StringComparison.Ordinal);
        Assert.Contains("000003,000004", result.Content, StringComparison.Ordinal);

        var chunks = derivativeRepository.ReadChunks(result.DerivativeId);
        Assert.Equal(["000001,000002", "000003,000004"], chunks.Select(chunk => chunk.SourceSegmentIds).ToArray());
    }

    [Fact]
    public async Task PolishAsync_ExtractsOutputSectionAndAddsSpeakerBlockBlankLines()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 5, 16, "Speaker_0", "raw one", "raw one"),
            new TranscriptSegment("000002", "job-001", 16, 64, "Speaker_1", "raw two", "raw two")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                # After

                。

                [00:05 - 00:16] Speaker_0: 入力側を繰り返した文です。
                Output:
                [00:05 - 00:16] Speaker_0: 読みやすい一文です。
                [00:16 - 01:04] Speaker_1: 次の話者ブロックです。
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal(
            $"[00:05 - 00:16] Speaker_0: 読みやすい一文です。{Environment.NewLine}{Environment.NewLine}[00:16 - 01:04] Speaker_1: 次の話者ブロックです。",
            result.Content);
        Assert.DoesNotContain("Output:", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("# After", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("入力側", result.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_UsesMarkedBlocksAndDropsTextOutsideMarkers()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 5, 16, "Speaker_0", "raw one", "raw one"),
            new TranscriptSegment("000002", "job-001", 16, 64, "Speaker_1", "raw two", "raw two")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                入力範囲外の前置きです。
                BEGIN_BLOCK block-001
                [00:05 - 00:16] Speaker_0: 読みやすい一文です。
                END_BLOCK block-001
                途中の余計な説明です。
                BEGIN_BLOCK block-002
                [00:16 - 01:04] Speaker_1: 次の話者ブロックです。
                END_BLOCK block-002
                入力範囲外の後書きです。
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal(
            $"[00:05 - 00:16] Speaker_0: 読みやすい一文です。{Environment.NewLine}{Environment.NewLine}[00:16 - 01:04] Speaker_1: 次の話者ブロックです。",
            result.Content);
        Assert.DoesNotContain("入力範囲外", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN_BLOCK", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("END_BLOCK", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolishAsync_DropsBareEndBlockAndConsecutiveDuplicateTailLines()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 1557, 1642, "Speaker_0", "raw", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                [25:57 - 27:22] Speaker_0: 本文です。
                締めの文です。
                END_BLOCK
                締めの文です。
                END_BLOCK
                締めの文です。
                END_BLOCK
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal(
            $"[25:57 - 27:22] Speaker_0: 本文です。{Environment.NewLine}締めの文です。",
            result.Content);
        Assert.DoesNotContain("END_BLOCK", result.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolishAsync_RecoversMarkedBlockOutputThatOmitsTimestamp()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 1642, 1644, "Speaker_0", "raw one", "raw one"),
            new TranscriptSegment("000002", "job-001", 1644, 1668, "Speaker_0", "raw two", "raw two")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                BEGIN_BLOCK block-001
                Speaker_0: Polished readable sentence.
                END_BLOCK block-001

                BEGIN_BLOCK block-002
                Speaker_0: Polished readable sentence.
                END_BLOCK block-002
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal("[27:22 - 27:48] Speaker_0: Polished readable sentence.", result.Content);
        Assert.DoesNotContain("raw one", result.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.DoesNotContain("fallback=", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_RecoversSingleMarkedBlockWithoutTimestampWhenItHasParagraphs()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 4.56, 10, "Speaker_0", "raw one", "raw one"),
            new TranscriptSegment("000002", "job-001", 10, 185.38, "Speaker_0", "raw two", "raw two")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                Unrelated preamble from the model.

                BEGIN_BLOCK block-001
                こんにちは、友育プロジェクト推進委員の上条です。

                本日は、育休中のリスクと対策についてお話しします。
                END_BLOCK block-001
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal(
            $"[00:04 - 03:05] Speaker_0: こんにちは、友育プロジェクト推進委員の上条です。{Environment.NewLine}{Environment.NewLine}本日は、育休中のリスクと対策についてお話しします。",
            result.Content);
        Assert.DoesNotContain("Unrelated preamble", result.Content, StringComparison.Ordinal);
        Assert.DoesNotContain("raw one", result.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.DoesNotContain("fallback=", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_RecoversJapaneseSpeakerMarkedBlockOutputThatUsesFullWidthColon()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 1642, 1644, "上嶋", "料理辛い風を乗り越える鍵となります", "料理辛い風を乗り越える鍵となります"),
            new TranscriptSegment("000002", "job-001", 1644, 1668, "上嶋", "ご視聴いただきありがとうございました", "ご視聴いただきありがとうございました")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                BEGIN_BLOCK block-001
                上嶋：読みやすく整えた本文です。
                END_BLOCK block-001
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal("[27:22 - 27:48] 上嶋: 読みやすく整えた本文です。", result.Content);
        Assert.DoesNotContain("料理辛い風", result.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.DoesNotContain("fallback=", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_FallsBackToSourceBlockWhenOutputRepeatsLongLines()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "raw", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                [00:00 - 00:01] Speaker_0: 本文です。
                同じ長い文が何度も繰り返されます。
                別の長い文を挟んで反復します。
                同じ長い文が何度も繰り返されます。
                別の長い文を挟んで反復します。
                同じ長い文が何度も繰り返されます。
                別の長い文を挟んで反復します。
                同じ長い文が何度も繰り返されます。
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal("[00:00 - 00:01] Speaker_0: raw", result.Content);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Contains("raw", derivative.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.Contains("fallback=repeated line", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_FallsBackToSourceBlockWhenOutputContainsReplacementCharacter()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "raw", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => "[00:00 - 00:01] Speaker_0: 壊れた�出力です。"));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal("[00:00 - 00:01] Speaker_0: raw", result.Content);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Contains("raw", derivative.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.Contains("fallback=contains replacement characters", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_FallsBackToSourceBlockWhenOutputAddsExtraTimestampedBlocks()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 1661.9, 1664.66, "Speaker_0", "please use the follow-up episode", "please use the follow-up episode"),
            new TranscriptSegment("000002", "job-001", 1664.66, 1666.9, "Speaker_0", "thank you for watching", "thank you for watching")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => """
                [27:41 - 27:47] Speaker_0: Please use the follow-up episode. Thank you for watching.

                [27:47 - 27:50] Speaker_0: See you in the next video. Thank you.
                """));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal(
            $"[27:41 - 27:46] Speaker_0: please use the follow-up episode{Environment.NewLine}thank you for watching",
            result.Content);
        Assert.DoesNotContain("See you in the next video", result.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.Contains("fallback=unexpected transcript block count", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_FallsBackToSourceBlockWhenOutputTimestampExceedsSourceBlock()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 1661.9, 1666.9, "Speaker_0", "thank you", "thank you")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => "[27:41 - 28:00] Speaker_0: Thank you. See you in the next video."));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal("[27:41 - 27:46] Speaker_0: thank you", result.Content);
        Assert.DoesNotContain("next video", result.Content, StringComparison.OrdinalIgnoreCase);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.Contains("fallback=timestamp outside source block", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_FallsBackToSourceBlockWhenRuntimeReturnsEmptyContent()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text", "text")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new FakePolishingRuntime(_ => "   "));

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Equal("[00:00 - 00:01] Speaker_0: text", result.Content);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Succeeded, derivative.Status);
        Assert.Equal("000001..000001", derivative.SourceSegmentRange);
        Assert.Contains("text", derivative.Content, StringComparison.Ordinal);
        var chunk = Assert.Single(derivativeRepository.ReadChunks(result.DerivativeId));
        Assert.Contains("fallback=empty", chunk.GenerationProfile, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_StoresFailedDerivativeWhenRuntimeThrows()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, [
            new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "text", "text")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(fixture.Paths);
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            derivativeRepository,
            new ThrowingPolishingRuntime());

        var result = await service.PolishAsync(CreateOptions("job-001"));

        Assert.Empty(result.Content);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Failed, derivative.Status);
        Assert.Equal("000001..000001", derivative.SourceSegmentRange);
        Assert.Contains("simulated runtime failure", derivative.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PolishAsync_RejectsMissingSegments()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var service = new TranscriptPolishingService(
            new TranscriptReadRepository(fixture.Paths),
            new TranscriptDerivativeRepository(fixture.Paths),
            new FakePolishingRuntime(_ => "unused"));

        var exception = await Assert.ThrowsAsync<ReviewWorkerException>(() => service.PolishAsync(CreateOptions("missing-job")));

        Assert.Equal(ReviewFailureCategory.MissingSegments, exception.Category);
    }

    [Fact]
    public void PromptBuilder_IncludesSafetyRulesAndSourceSegments()
    {
        var prompt = new TranscriptPolishingPromptBuilder().Build(new TranscriptPolishingChunk(
            1,
            [
                new TranscriptReadModel("000001", 0, 1, "Speaker_0", "えー今日はテストです", "none", "Speaker_0", "えー今日はテストです", null, null)
            ]),
            TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId);

        Assert.Contains("Do not add facts", prompt, StringComparison.Ordinal);
        Assert.Contains("speaker_block_id: block-001", prompt, StringComparison.Ordinal);
        Assert.Contains("source_segment_ids: 000001", prompt, StringComparison.Ordinal);
        Assert.Contains("timestamp: 00:00 - 00:01", prompt, StringComparison.Ordinal);
        Assert.Contains("Speaker_0", prompt, StringComparison.Ordinal);
        Assert.Contains("combined_text: |", prompt, StringComparison.Ordinal);
        Assert.Contains("add Japanese punctuation", prompt, StringComparison.Ordinal);
        Assert.Contains("split sentences at natural boundaries", prompt, StringComparison.Ordinal);
        Assert.Contains("plain text only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not output Markdown fences, explanations, JSON, headings, or an \"Output:\" label", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not repeat the input transcript", prompt, StringComparison.Ordinal);
        Assert.Contains("Output exactly one result block for each input speaker_block_id", prompt, StringComparison.Ordinal);
        Assert.Contains("BEGIN_BLOCK block-001", prompt, StringComparison.Ordinal);
        Assert.Contains("END_BLOCK block-001", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_UsesCompactPromptForBonsai()
    {
        var prompt = new TranscriptPolishingPromptBuilder().Build(new TranscriptPolishingChunk(
            1,
            [
                new TranscriptReadModel("000001", 0, 1, "Speaker_0", "えー今日はテストです", "none", "Speaker_0", "えー今日はテストです", null, null)
            ]),
            TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId);

        Assert.Contains("short Japanese ASR transcript chunk", prompt, StringComparison.Ordinal);
        Assert.Contains("Keep wording close to the input", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not summarize, expand, or restructure", prompt, StringComparison.Ordinal);
        Assert.Contains("BEGIN_BLOCK block-001", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_UsesJapanesePromptForLlmJp()
    {
        var prompt = new TranscriptPolishingPromptBuilder().Build(new TranscriptPolishingChunk(
            1,
            [
                new TranscriptReadModel("000001", 0, 1, "Speaker_0", "えー今日はテストです", "none", "Speaker_0", "えー今日はテストです", null, null)
            ]),
            TranscriptPolishingPromptBuilder.LlmJpPromptTemplateId);

        Assert.Contains("あなたは日本語の音声認識結果を読みやすく整える編集者です", prompt, StringComparison.Ordinal);
        Assert.Contains("要約や大幅な言い換えはしないでください", prompt, StringComparison.Ordinal);
        Assert.Contains("BEGIN_BLOCK block-001", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void PromptBuilder_GroupsConsecutiveSegmentsBySpeakerBlocks()
    {
        var prompt = new TranscriptPolishingPromptBuilder().Build(new TranscriptPolishingChunk(
            1,
            [
                new TranscriptReadModel("000001", 0, 1, "Speaker_0", "first", "none", "Speaker_0", "first", null, null),
                new TranscriptReadModel("000002", 1, 2.5, "Speaker_0", "second", "none", "Speaker_0", "second", null, null),
                new TranscriptReadModel("000003", 3, 4, "Speaker_1", "third", "none", "Speaker_1", "third", null, null),
                new TranscriptReadModel("000004", 4, 5, "Speaker_0", "fourth", "none", "Speaker_0", "fourth", null, null)
            ]));

        Assert.Contains("speaker_block_id: block-001", prompt, StringComparison.Ordinal);
        Assert.Contains("source_segment_ids: 000001,000002", prompt, StringComparison.Ordinal);
        Assert.Contains("timestamp: 00:00 - 00:02", prompt, StringComparison.Ordinal);
        Assert.Contains("speaker_block_id: block-002", prompt, StringComparison.Ordinal);
        Assert.Contains("source_segment_ids: 000003", prompt, StringComparison.Ordinal);
        Assert.Contains("speaker: Speaker_1", prompt, StringComparison.Ordinal);
        Assert.Contains("speaker_block_id: block-003", prompt, StringComparison.Ordinal);
        Assert.Contains("source_segment_ids: 000004", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("source_segment_ids: 000001,000002,000004", prompt, StringComparison.Ordinal);
        Assert.Contains("Treat each speaker block as one continuous utterance", prompt, StringComparison.Ordinal);
        Assert.Contains("Do not keep source segment line breaks", prompt, StringComparison.Ordinal);
    }

    private static TranscriptPolishingOptions CreateOptions(string jobId, int chunkSegmentCount = 80)
    {
        return new TranscriptPolishingOptions(
            jobId,
            "llama-completion.exe",
            "model.gguf",
            Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N")),
            "bonsai-8b-q1-0",
            TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId,
            "lightweight",
            ChunkSegmentCount: chunkSegmentCount);
    }

    private static void SaveSegments(AppPaths paths, IReadOnlyList<TranscriptSegment> segments)
    {
        new TranscriptSegmentRepository(paths).SaveSegments(segments);
    }

    private static string FormatTestTimestamp(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private sealed class FakePolishingRuntime(Func<TranscriptPolishingChunk, string> responseFactory) : ITranscriptPolishingRuntime
    {
        public Task<TranscriptPolishingChunkResult> PolishChunkAsync(
            TranscriptPolishingOptions options,
            TranscriptPolishingChunk chunk,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new TranscriptPolishingChunkResult(
                chunk,
                responseFactory(chunk),
                TimeSpan.FromMilliseconds(10)));
        }
    }

    private sealed class ThrowingPolishingRuntime : ITranscriptPolishingRuntime
    {
        public Task<TranscriptPolishingChunkResult> PolishChunkAsync(
            TranscriptPolishingOptions options,
            TranscriptPolishingChunk chunk,
            CancellationToken cancellationToken = default)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.ProcessFailed, "simulated runtime failure");
        }
    }
}
