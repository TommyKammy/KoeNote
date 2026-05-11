namespace KoeNote.App.Services.Transcript;

public sealed class TranscriptPolishingPromptBuilder
{
    public const string PromptVersion = "polish-v3-speaker-block-markers";
    public const string GemmaBlockPromptTemplateId = "gemma-polishing-blocks";
    public const string BonsaiCompactPromptTemplateId = "bonsai-polishing-compact";
    public const string LlmJpPromptTemplateId = "llm-jp-polishing-ja";

    public string Build(TranscriptPolishingChunk chunk, string promptTemplateId = GemmaBlockPromptTemplateId)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var source = TranscriptPolishingPromptSourceBuilder.Build(chunk.Segments);

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

}
