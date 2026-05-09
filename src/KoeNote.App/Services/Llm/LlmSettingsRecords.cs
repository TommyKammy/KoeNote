namespace KoeNote.App.Services.Llm;

public enum LlmTaskKind
{
    Review,
    Summary,
    Polishing
}

public sealed record LlmRuntimeProfile(
    string ProfileId,
    string ModelId,
    string? ModelFamily,
    string DisplayName,
    string RuntimeKind,
    string RuntimePackageId,
    string ModelPath,
    string LlamaCompletionPath,
    int ContextSize,
    int GpuLayers,
    int? Threads,
    int? ThreadsBatch,
    bool NoConversation,
    string OutputSanitizerProfile,
    TimeSpan Timeout)
{
    public string AccelerationMode { get; init; } = "cpu";
}

public sealed record LlmTaskSettings(
    LlmTaskKind TaskKind,
    string PromptTemplateId,
    string PromptVersion,
    string GenerationProfile,
    double Temperature,
    double? TopP,
    int? TopK,
    double? RepeatPenalty,
    int MaxTokens,
    int ChunkSegmentCount,
    int ChunkOverlap,
    bool UseJsonSchema,
    bool EnableRepair,
    string ValidationMode);
