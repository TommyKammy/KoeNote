namespace KoeNote.App.Services.Transcript;

public sealed record TranscriptSummaryOptions(
    string JobId,
    string LlamaCompletionPath,
    string ModelPath,
    string OutputDirectory,
    string ModelId,
    string GenerationProfile = "recommended",
    string PromptVersion = TranscriptSummaryPromptBuilder.PromptVersion,
    int ChunkSegmentCount = 80,
    TimeSpan? Timeout = null,
    string OutputSanitizerProfile = LlmOutputSanitizerProfiles.None,
    int ContextSize = 8192,
    int GpuLayers = 999,
    int MaxTokens = 1024,
    int? Threads = null,
    int? ThreadsBatch = null);

public sealed record TranscriptSummaryChunk(
    int ChunkIndex,
    string SourceKind,
    string SourceSegmentIds,
    double? SourceStartSeconds,
    double? SourceEndSeconds,
    string Content);

public sealed record TranscriptSummaryChunkResult(
    TranscriptSummaryChunk Chunk,
    string Content,
    TimeSpan Duration);

public sealed record TranscriptSummaryResult(
    string JobId,
    string DerivativeId,
    string Content,
    string SourceKind,
    string SourceTranscriptHash,
    int ChunkCount,
    TimeSpan Duration);

public interface ITranscriptSummaryRuntime
{
    Task<TranscriptSummaryChunkResult> SummarizeChunkAsync(
        TranscriptSummaryOptions options,
        TranscriptSummaryChunk chunk,
        CancellationToken cancellationToken = default);

    Task<string> MergeSummariesAsync(
        TranscriptSummaryOptions options,
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        CancellationToken cancellationToken = default);
}
