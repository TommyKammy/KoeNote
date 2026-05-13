namespace KoeNote.App.Services.Transcript;

public static class ReadablePolishingPromptModelFamilies
{
    public const string Gemma = "gemma";
    public const string Bonsai = "bonsai";
    public const string LlmJp = "llm-jp";

    public static IReadOnlyList<string> Supported { get; } = [Gemma, Bonsai, LlmJp];

    public static string Normalize(string? modelFamily)
    {
        var normalized = (modelFamily ?? string.Empty).Trim().ToLowerInvariant();
        return Supported.Contains(normalized, StringComparer.Ordinal) ? normalized : Gemma;
    }

    public static string ResolveForModel(string? modelId, string? modelFamily)
    {
        if (Matches(modelId, modelFamily, Bonsai))
        {
            return Bonsai;
        }

        if (Matches(modelId, modelFamily, LlmJp))
        {
            return LlmJp;
        }

        if (Matches(modelId, modelFamily, Gemma))
        {
            return Gemma;
        }

        return Normalize(modelFamily);
    }

    private static bool Matches(string? modelId, string? modelFamily, string value)
    {
        return (!string.IsNullOrWhiteSpace(modelId) && modelId.Contains(value, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(modelFamily) && modelFamily.Contains(value, StringComparison.OrdinalIgnoreCase));
    }
}

public static class ReadablePolishingPromptPresets
{
    public const string Standard = "standard";
    public const string StrongPunctuation = "strong-punctuation";
    public const string Faithful = "faithful";
    public const string MeetingMinutes = "meeting-minutes";
    public const string LectureSeminar = "lecture-seminar";
    public const string Custom = "custom";
    public const string Default = StrongPunctuation;

    public static IReadOnlyList<string> Supported { get; } =
    [
        Standard,
        StrongPunctuation,
        Faithful,
        MeetingMinutes,
        LectureSeminar,
        Custom
    ];

    public static string Normalize(string? presetId)
    {
        var normalized = (presetId ?? string.Empty).Trim().ToLowerInvariant();
        return Supported.Contains(normalized, StringComparer.Ordinal) ? normalized : Standard;
    }
}

public sealed record ReadablePolishingPromptSettings(
    string ModelFamily,
    string PresetId,
    string AdditionalInstruction,
    bool UseCustomPrompt,
    string CustomPrompt,
    string PromptTemplateId,
    string PromptVersion)
{
    public static ReadablePolishingPromptSettings CreateDefault(string modelFamily)
    {
        var normalizedFamily = ReadablePolishingPromptModelFamilies.Normalize(modelFamily);
        return new ReadablePolishingPromptSettings(
            normalizedFamily,
            ReadablePolishingPromptPresets.Default,
            string.Empty,
            UseCustomPrompt: false,
            string.Empty,
            ResolveDefaultPromptTemplateId(normalizedFamily),
            TranscriptPolishingPromptBuilder.PromptVersion);
    }

    public ReadablePolishingPromptSettings Normalize()
    {
        var normalizedFamily = ReadablePolishingPromptModelFamilies.Normalize(ModelFamily);
        var normalizedPreset = ReadablePolishingPromptPresets.Normalize(PresetId);
        var normalizedUseCustomPrompt = UseCustomPrompt && !string.IsNullOrWhiteSpace(CustomPrompt);
        return this with
        {
            ModelFamily = normalizedFamily,
            PresetId = normalizedUseCustomPrompt ? ReadablePolishingPromptPresets.Custom : normalizedPreset,
            AdditionalInstruction = AdditionalInstruction.Trim(),
            UseCustomPrompt = normalizedUseCustomPrompt,
            CustomPrompt = CustomPrompt.Trim(),
            PromptTemplateId = string.IsNullOrWhiteSpace(PromptTemplateId)
                ? ResolveDefaultPromptTemplateId(normalizedFamily)
                : PromptTemplateId.Trim(),
            PromptVersion = string.IsNullOrWhiteSpace(PromptVersion)
                ? TranscriptPolishingPromptBuilder.PromptVersion
                : PromptVersion.Trim()
        };
    }

    public static string ResolveDefaultPromptTemplateId(string modelFamily)
    {
        return ReadablePolishingPromptModelFamilies.Normalize(modelFamily) switch
        {
            ReadablePolishingPromptModelFamilies.Bonsai => TranscriptPolishingPromptBuilder.BonsaiCompactPromptTemplateId,
            ReadablePolishingPromptModelFamilies.LlmJp => TranscriptPolishingPromptBuilder.LlmJpPromptTemplateId,
            _ => TranscriptPolishingPromptBuilder.GemmaBlockPromptTemplateId
        };
    }
}

public sealed record PersistedReadablePolishingPromptSettings(
    ReadablePolishingPromptSettings Settings,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
