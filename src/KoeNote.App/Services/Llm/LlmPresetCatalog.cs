using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Llm;

public sealed record LlmRuntimePreset(
    string PresetId,
    TimeSpan Timeout,
    int ContextSize,
    int GpuLayers,
    int? Threads,
    int? ThreadsBatch,
    bool NoConversation);

public static class LlmPresetCatalog
{
    public static LlmRuntimePreset ResolveRuntimePreset(string modelId, string? family)
    {
        if (IsTernaryBonsai(modelId, family))
        {
            return new LlmRuntimePreset(
                "ternary-bonsai:cpu-bounded",
                TimeSpan.FromMinutes(20),
                ContextSize: 1024,
                GpuLayers: 0,
                Threads: GetCpuThreadCount(),
                ThreadsBatch: GetCpuThreadCount(),
                NoConversation: true);
        }

        if (IsGemma(modelId, family))
        {
            return Standard("gemma:balanced");
        }

        if (IsBonsai(modelId, family))
        {
            return Standard("bonsai:conservative");
        }

        if (IsLlmJp(modelId, family))
        {
            return Standard("llm-jp:balanced");
        }

        return Standard("fallback:balanced");
    }

    public static LlmTaskSettings ResolveTaskSettings(string modelId, string? family, LlmTaskKind taskKind)
    {
        return taskKind switch
        {
            LlmTaskKind.Review => ResolveReviewSettings(modelId, family),
            LlmTaskKind.Summary => ResolveSummarySettings(modelId, family),
            LlmTaskKind.Polishing => ResolvePolishingSettings(modelId, family),
            _ => throw new ArgumentOutOfRangeException(nameof(taskKind), taskKind, "Unsupported LLM task kind.")
        };
    }

    private static LlmTaskSettings ResolveReviewSettings(string modelId, string? family)
    {
        if (IsTernaryBonsai(modelId, family))
        {
            return new LlmTaskSettings(
                LlmTaskKind.Review,
                PromptTemplateId: "compact",
                PromptVersion: "current",
                GenerationProfile: "ternary-bonsai-review",
                Temperature: 0.1,
                TopP: null,
                TopK: null,
                RepeatPenalty: null,
                MaxTokens: 192,
                ChunkSegmentCount: 3,
                ChunkOverlap: 0,
                UseJsonSchema: false,
                EnableRepair: false,
                ValidationMode: "json_parse");
        }

        if (IsBonsai(modelId, family))
        {
            return new LlmTaskSettings(
                LlmTaskKind.Review,
                PromptTemplateId: "default",
                PromptVersion: "current",
                GenerationProfile: "bonsai-review-conservative",
                Temperature: 0.1,
                TopP: null,
                TopK: null,
                RepeatPenalty: null,
                MaxTokens: 4096,
                ChunkSegmentCount: 40,
                ChunkOverlap: 0,
                UseJsonSchema: true,
                EnableRepair: true,
                ValidationMode: "json_schema");
        }

        return new LlmTaskSettings(
            LlmTaskKind.Review,
            PromptTemplateId: "default",
            PromptVersion: "current",
            GenerationProfile: ResolveGenerationProfile(modelId, family, "review"),
            Temperature: 0.1,
            TopP: null,
            TopK: null,
            RepeatPenalty: null,
            MaxTokens: 4096,
            ChunkSegmentCount: 80,
            ChunkOverlap: 0,
            UseJsonSchema: true,
            EnableRepair: true,
            ValidationMode: "json_schema");
    }

    private static LlmTaskSettings ResolveSummarySettings(string modelId, string? family)
    {
        if (IsTernaryBonsai(modelId, family))
        {
            return Summary(
                "bonsai-compact",
                "ternary-bonsai-summary",
                maxTokens: 512,
                chunkSegmentCount: 3);
        }

        if (IsBonsai(modelId, family))
        {
            return Summary(
                "bonsai-compact",
                "bonsai-summary-conservative",
                maxTokens: 512,
                chunkSegmentCount: 40);
        }

        return Summary(
            ResolveSummaryPromptTemplateId(modelId, family),
            ResolveGenerationProfile(modelId, family, "summary"),
            maxTokens: IsGemma(modelId, family) ? 1024 : 768,
            chunkSegmentCount: 80);
    }

    private static LlmTaskSettings ResolvePolishingSettings(string modelId, string? family)
    {
        if (IsTernaryBonsai(modelId, family))
        {
            return Polishing(
                TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId,
                "ternary-bonsai-polishing",
                maxTokens: 192,
                chunkSegmentCount: 3,
                repeatPenalty: 1.15);
        }

        if (IsBonsai(modelId, family))
        {
            return Polishing(
                TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId,
                "bonsai-polishing-conservative",
                maxTokens: 1024,
                chunkSegmentCount: 8,
                repeatPenalty: 1.15);
        }

        if (IsLlmJp(modelId, family))
        {
            return Polishing(
                TranscriptPolishingPromptBuilder.LlmJpPromptTemplateId,
                ResolveGenerationProfile(modelId, family, "polishing"),
                maxTokens: 2048,
                chunkSegmentCount: 20);
        }

        return Polishing(
            IsGemma(modelId, family) ? TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId : "default",
            ResolveGenerationProfile(modelId, family, "polishing"),
            maxTokens: 4096,
            chunkSegmentCount: 40);
    }

    private static LlmTaskSettings Summary(
        string promptTemplateId,
        string generationProfile,
        int maxTokens,
        int chunkSegmentCount)
    {
        return new LlmTaskSettings(
            LlmTaskKind.Summary,
            PromptTemplateId: promptTemplateId,
            PromptVersion: TranscriptSummaryPromptBuilder.PromptVersion,
            GenerationProfile: generationProfile,
            Temperature: 0.1,
            TopP: null,
            TopK: null,
            RepeatPenalty: null,
            MaxTokens: maxTokens,
            ChunkSegmentCount: chunkSegmentCount,
            ChunkOverlap: 0,
            UseJsonSchema: false,
            EnableRepair: false,
            ValidationMode: "markdown_summary_sections");
    }

    private static LlmTaskSettings Polishing(
        string promptTemplateId,
        string generationProfile,
        int maxTokens,
        int chunkSegmentCount,
        double? repeatPenalty = null)
    {
        return new LlmTaskSettings(
            LlmTaskKind.Polishing,
            PromptTemplateId: promptTemplateId,
            PromptVersion: TranscriptPolishingPromptBuilder.PromptVersion,
            GenerationProfile: generationProfile,
            Temperature: 0.1,
            TopP: null,
            TopK: null,
            RepeatPenalty: repeatPenalty,
            MaxTokens: maxTokens,
            ChunkSegmentCount: chunkSegmentCount,
            ChunkOverlap: 0,
            UseJsonSchema: false,
            EnableRepair: false,
            ValidationMode: "markdown_non_empty");
    }

    private static LlmRuntimePreset Standard(string presetId)
    {
        return new LlmRuntimePreset(
            presetId,
            TimeSpan.FromHours(2),
            ContextSize: 8192,
            GpuLayers: 999,
            Threads: null,
            ThreadsBatch: null,
            NoConversation: true);
    }

    private static string ResolveGenerationProfile(string modelId, string? family, string taskName)
    {
        if (IsGemma(modelId, family))
        {
            return $"gemma-{taskName}-balanced";
        }

        if (IsLlmJp(modelId, family))
        {
            return $"llm-jp-{taskName}-balanced";
        }

        return $"fallback-{taskName}-balanced";
    }

    private static string ResolveSummaryPromptTemplateId(string modelId, string? family)
    {
        if (IsGemma(modelId, family))
        {
            return "gemma-structured";
        }

        if (IsLlmJp(modelId, family))
        {
            return "llm-jp-structured";
        }

        return "default";
    }

    private static bool IsTernaryBonsai(string modelId, string? family)
    {
        return IsBonsai(modelId, family) && modelId.Contains("ternary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBonsai(string modelId, string? family)
    {
        return Matches(modelId, family, "bonsai");
    }

    private static bool IsGemma(string modelId, string? family)
    {
        return Matches(modelId, family, "gemma");
    }

    private static bool IsLlmJp(string modelId, string? family)
    {
        return Matches(modelId, family, "llm-jp");
    }

    private static bool Matches(string modelId, string? family, string value)
    {
        return modelId.Contains(value, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(family) && family.Contains(value, StringComparison.OrdinalIgnoreCase));
    }

    private static int GetCpuThreadCount()
    {
        return Math.Clamp(Environment.ProcessorCount, 1, 8);
    }
}
