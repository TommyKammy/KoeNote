using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class TranscriptDerivativeRepositoryTests
{
    [Fact]
    public void Save_StoresAndReadsLatestSuccessfulDerivative()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, "first text");
        var repository = new TranscriptDerivativeRepository(fixture.Paths);
        var sourceHash = repository.ComputeCurrentRawTranscriptHash("job-001");

        var derivative = repository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "First text.",
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            "000001..000001",
            null,
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight"));

        var latest = repository.ReadLatestSuccessful("job-001", TranscriptDerivativeKinds.Polished);

        Assert.NotNull(latest);
        Assert.Equal(derivative.DerivativeId, latest.DerivativeId);
        Assert.Equal("First text.", latest.Content);
        Assert.Equal(sourceHash, latest.SourceTranscriptHash);
        Assert.False(repository.IsStale(latest));
    }

    [Fact]
    public void SaveChunk_StoresChunksInChunkIndexOrder()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, "first text");
        var repository = new TranscriptDerivativeRepository(fixture.Paths);
        var sourceHash = repository.ComputeCurrentRawTranscriptHash("job-001");
        var derivative = repository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "First text. Second text.",
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            "000001..000002",
            "chunk-2,chunk-1",
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight"));

        repository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
            derivative.DerivativeId,
            "job-001",
            2,
            TranscriptDerivativeSourceKinds.Raw,
            "000002",
            1,
            2,
            sourceHash,
            TranscriptDerivativeFormats.PlainText,
            "Second text.",
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight",
            ChunkId: "chunk-2"));
        repository.SaveChunk(new TranscriptDerivativeChunkSaveRequest(
            derivative.DerivativeId,
            "job-001",
            1,
            TranscriptDerivativeSourceKinds.Raw,
            "000001",
            0,
            1,
            sourceHash,
            TranscriptDerivativeFormats.PlainText,
            "First text.",
            "bonsai-8b-q1-0",
            "polish-v1",
            "lightweight",
            ChunkId: "chunk-1"));

        var chunks = repository.ReadChunks(derivative.DerivativeId);

        Assert.Equal(["chunk-1", "chunk-2"], chunks.Select(chunk => chunk.ChunkId).ToArray());
        Assert.Equal(["000001", "000002"], chunks.Select(chunk => chunk.SourceSegmentIds).ToArray());
    }

    [Fact]
    public void IsStale_ReturnsTrueWhenRawTranscriptChanges()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, "first text");
        var repository = new TranscriptDerivativeRepository(fixture.Paths);
        var sourceHash = repository.ComputeCurrentRawTranscriptHash("job-001");
        var derivative = repository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            "## Overview\n\nFirst text.",
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            "000001..000001",
            null,
            "bonsai-8b-q1-0",
            "summary-v1",
            "lightweight"));

        SaveSegments(fixture.Paths, "changed text");

        Assert.True(repository.IsStale(derivative));
    }

    [Fact]
    public void MarkStaleForJob_MarksOutdatedRawDerivatives()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, "first text");
        var repository = new TranscriptDerivativeRepository(fixture.Paths);
        var oldHash = repository.ComputeCurrentRawTranscriptHash("job-001");
        repository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            "old summary",
            TranscriptDerivativeSourceKinds.Raw,
            oldHash,
            "000001..000001",
            null,
            "bonsai-8b-q1-0",
            "summary-v1",
            "lightweight",
            DerivativeId: "summary-old"));
        SaveSegments(fixture.Paths, "changed text");
        var newHash = repository.ComputeCurrentRawTranscriptHash("job-001");

        var updated = repository.MarkStaleForJob("job-001", newHash);
        var derivative = repository.ReadById("summary-old");

        Assert.Equal(1, updated);
        Assert.NotNull(derivative);
        Assert.Equal(TranscriptDerivativeStatuses.Stale, derivative.Status);
    }

    [Fact]
    public void Save_AllowsFailedDerivativeWithEmptyContent()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, "first text");
        var repository = new TranscriptDerivativeRepository(fixture.Paths);
        var sourceHash = repository.ComputeCurrentRawTranscriptHash("job-001");

        var derivative = repository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            string.Empty,
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            "000001..000001",
            null,
            "bonsai-8b-q1-0",
            "summary-v1",
            "lightweight",
            TranscriptDerivativeStatuses.Failed,
            "LLM returned empty output."));

        Assert.Equal(TranscriptDerivativeStatuses.Failed, derivative.Status);
        Assert.Empty(derivative.Content);
        Assert.Equal("LLM returned empty output.", derivative.ErrorMessage);
    }

    [Fact]
    public void Save_RejectsSuccessfulDerivativeWithEmptyContent()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        SaveSegments(fixture.Paths, "first text");
        var repository = new TranscriptDerivativeRepository(fixture.Paths);
        var sourceHash = repository.ComputeCurrentRawTranscriptHash("job-001");

        Assert.Throws<ArgumentException>(() => repository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Summary,
            TranscriptDerivativeFormats.Markdown,
            string.Empty,
            TranscriptDerivativeSourceKinds.Raw,
            sourceHash,
            "000001..000001",
            null,
            "bonsai-8b-q1-0",
            "summary-v1",
            "lightweight")));
    }

    private static void SaveSegments(AppPaths paths, string text)
    {
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment(
                "000001",
                "job-001",
                0,
                1,
                "Speaker_0",
                text,
                text,
                Source: "asr",
                AsrRunId: "run-001")
        ]);
    }
}
