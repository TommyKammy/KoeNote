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
            $"[{chunk.ChunkIndex}] {string.Join(" / ", chunk.Segments.Select(segment => segment.Text))}");
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
    public async Task PolishAsync_StoresFailedDerivativeWhenRuntimeReturnsEmptyContent()
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

        Assert.Empty(result.Content);
        var derivative = derivativeRepository.ReadById(result.DerivativeId);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Failed, derivative.Status);
        Assert.Equal("000001..000001", derivative.SourceSegmentRange);
        Assert.Equal("Transcript polishing returned empty output.", derivative.ErrorMessage);
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
            ]));

        Assert.Contains("Do not add facts", prompt, StringComparison.Ordinal);
        Assert.Contains("segment_id: 000001", prompt, StringComparison.Ordinal);
        Assert.Contains("timestamp: 00:00", prompt, StringComparison.Ordinal);
        Assert.Contains("Speaker_0", prompt, StringComparison.Ordinal);
        Assert.Contains("plain text only", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private static TranscriptPolishingOptions CreateOptions(string jobId, int chunkSegmentCount = 80)
    {
        return new TranscriptPolishingOptions(
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
