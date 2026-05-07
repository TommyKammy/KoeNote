namespace KoeNote.App.Services.Review;

public sealed record ReviewRuntimeTuning(
    TimeSpan Timeout,
    int ContextSize,
    int GpuLayers,
    int MaxTokens,
    int ChunkSegmentCount,
    int? Threads,
    int? ThreadsBatch,
    bool UseJsonSchema,
    bool EnableRepair,
    string PromptProfile);

public static class ReviewRuntimeTuningProfiles
{
    public static ReviewRuntimeTuning ForReviewModel(string modelId)
    {
        return IsTernaryModel(modelId)
            ? new ReviewRuntimeTuning(
                TimeSpan.FromMinutes(4),
                ContextSize: 1024,
                GpuLayers: 0,
                MaxTokens: 192,
                ChunkSegmentCount: 3,
                Threads: GetCpuThreadCount(),
                ThreadsBatch: GetCpuThreadCount(),
                UseJsonSchema: false,
                EnableRepair: false,
                PromptProfile: "compact")
            : new ReviewRuntimeTuning(
                TimeSpan.FromHours(2),
                ContextSize: 8192,
                GpuLayers: 999,
                MaxTokens: 4096,
                ChunkSegmentCount: 80,
                Threads: null,
                ThreadsBatch: null,
                UseJsonSchema: true,
                EnableRepair: true,
                PromptProfile: "default");
    }

    public static bool IsTernaryModel(string modelId)
    {
        return modelId.Contains("ternary", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetCpuThreadCount()
    {
        return Math.Clamp(Environment.ProcessorCount, 1, 8);
    }
}
