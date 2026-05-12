namespace KoeNote.App.Services.Transcript;

public sealed record TranscriptPolishingOptions(
    string JobId,
    string LlamaCompletionPath,
    string ModelPath,
    string OutputDirectory,
    string ModelId,
    string PromptTemplateId = TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId,
    string GenerationProfile = "recommended",
    string PromptVersion = TranscriptPolishingPromptBuilder.PromptVersion,
    int ChunkSegmentCount = 80,
    TimeSpan? Timeout = null,
    string OutputSanitizerProfile = LlmOutputSanitizerProfiles.None,
    int ContextSize = 8192,
    int GpuLayers = 999,
    int MaxTokens = 4096,
    double Temperature = 0.1,
    double? TopP = null,
    int? TopK = null,
    double? RepeatPenalty = null,
    bool NoConversation = true,
    int? Threads = null,
    int? ThreadsBatch = null,
    ReadablePolishingPromptSettings? PromptSettings = null);

public sealed record TranscriptPolishingChunk(
    int ChunkIndex,
    IReadOnlyList<TranscriptReadModel> Segments)
{
    public string SourceSegmentIds => string.Join(",", Segments.Select(static segment => segment.SegmentId));
    public double? SourceStartSeconds => Segments.Count == 0 ? null : Segments.Min(static segment => segment.StartSeconds);
    public double? SourceEndSeconds => Segments.Count == 0 ? null : Segments.Max(static segment => segment.EndSeconds);
}

public sealed record TranscriptPolishingChunkResult(
    TranscriptPolishingChunk Chunk,
    string Content,
    TimeSpan Duration,
    bool UsedFallback = false,
    string? FallbackReason = null);

public sealed record TranscriptPolishingResult(
    string JobId,
    string DerivativeId,
    string Content,
    string SourceTranscriptHash,
    int ChunkCount,
    TimeSpan Duration);

public interface ITranscriptPolishingRuntime
{
    Task<TranscriptPolishingChunkResult> PolishChunkAsync(
        TranscriptPolishingOptions options,
        TranscriptPolishingChunk chunk,
        CancellationToken cancellationToken = default);
}
