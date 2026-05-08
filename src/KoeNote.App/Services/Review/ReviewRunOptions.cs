using KoeNote.App.Models;

namespace KoeNote.App.Services.Review;

public sealed record ReviewRunOptions(
    string JobId,
    string LlamaCompletionPath,
    string ModelPath,
    string OutputDirectory,
    IReadOnlyList<TranscriptSegment> Segments,
    double MinConfidence = 0.5,
    TimeSpan? Timeout = null,
    string ModelId = "",
    string OutputSanitizerProfile = "none",
    int ContextSize = 8192,
    int GpuLayers = 999,
    int MaxTokens = 4096,
    int ChunkSegmentCount = 80,
    double Temperature = 0.1,
    double? TopP = null,
    int? TopK = null,
    double? RepeatPenalty = null,
    bool NoConversation = true,
    int? Threads = null,
    int? ThreadsBatch = null,
    bool UseJsonSchema = true,
    bool EnableRepair = true,
    string PromptProfile = "default");
