namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptPolishingPromptBuilder
{
    public const string PromptVersion = "polish-v3-speaker-block-markers";
    public const string GemmaBlockPromptTemplateId = "gemma-polishing-blocks";
    public const string BonsaiCompactPromptTemplateId = "bonsai-polishing-compact";
    public const string LlmJpPromptTemplateId = "llm-jp-polishing-ja";

    public string Build(
        TranscriptPolishingChunk chunk,
        string promptTemplateId = GemmaBlockPromptTemplateId,
        ReadablePolishingPromptSettings? promptSettings = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var source = TranscriptPolishingPromptSourceBuilder.Build(chunk.Segments);
        var settings = promptSettings?.Normalize();
        if (settings?.UseCustomPrompt == true)
        {
            return BuildCustomPrompt(source, settings);
        }

        var effectiveTemplateId = string.IsNullOrWhiteSpace(settings?.PromptTemplateId)
            ? promptTemplateId
            : settings.PromptTemplateId;
        var prompt = BuildTemplatePrompt(source, effectiveTemplateId);
        var additionalInstruction = ResolveAdditionalInstruction(settings);
        return string.IsNullOrWhiteSpace(additionalInstruction)
            ? prompt
            : InjectAdditionalInstructions(prompt, additionalInstruction);
    }

    private static string BuildTemplatePrompt(string source, string promptTemplateId)
    {
        if (string.Equals(promptTemplateId, BonsaiCompactPromptTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return BonsaiPolishingPromptTemplate.Build(source);
        }

        if (string.Equals(promptTemplateId, LlmJpPromptTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return LlmJpPolishingPromptTemplate.Build(source);
        }

        return GemmaPolishingPromptTemplate.Build(source);
    }

    private static string InjectAdditionalInstructions(string prompt, string additionalInstruction)
    {
        var instructionLines = additionalInstruction
            .Trim()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => $"- {line}");

        var instructionBlock = $"""
            Additional user instructions:
            {string.Join(Environment.NewLine, instructionLines)}

            """;
        const string marker = "Speaker blocks:";
        var markerIndex = prompt.IndexOf(marker, StringComparison.Ordinal);
        return markerIndex < 0
            ? $"{prompt.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{instructionBlock}".TrimEnd()
            : prompt.Insert(markerIndex, instructionBlock);
    }

    private static string ResolveAdditionalInstruction(ReadablePolishingPromptSettings? settings)
    {
        if (settings is null)
        {
            return string.Empty;
        }

        var presetInstruction = ReadablePolishingPromptPresetInstructions.Resolve(settings);
        var userInstruction = settings.AdditionalInstruction.Trim();
        return string.Join(
            Environment.NewLine,
            new[] { presetInstruction, userInstruction }.Where(instruction => !string.IsNullOrWhiteSpace(instruction)));
    }

    private static string BuildCustomPrompt(string source, ReadablePolishingPromptSettings settings)
    {
        return $$"""
            {{settings.CustomPrompt.Trim()}}

            Mandatory output contract:
            - Process only the speaker blocks shown below.
            - Preserve speaker labels, timestamps, and block order.
            - Output exactly one result block for each input speaker_block_id.
            - Do not output content before the first BEGIN_BLOCK line or after the final END_BLOCK line.
            - Do not output text from any previous or later chunk.
            - Do not add facts that are not present in the transcript.
            - Do not guess uncertain names, numbers, dates, prices, decisions, owners, or deadlines.
            - Output plain text only. Do not output Markdown fences, explanations, JSON, headings, or an "Output:" label.
            - Do not repeat the input transcript or speaker block metadata.
            - Put one blank line between result blocks.

            Output format:
            BEGIN_BLOCK block-001
            [HH:MM:SS - HH:MM:SS] Speaker: polished utterance
            END_BLOCK block-001

            Speaker blocks:
            {{source}}
            """;
    }
}
