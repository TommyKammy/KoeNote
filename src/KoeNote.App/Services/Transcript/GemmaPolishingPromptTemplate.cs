namespace KoeNote.App.Services.Transcript;

internal static class GemmaPolishingPromptTemplate
{
    public static string Build(string source)
    {
        return $$"""
            You are polishing a Japanese ASR transcript for readability.

            Task:
            - Rewrite the transcript as readable prose.
            - Preserve speaker labels and segment order.
            - Treat each speaker block as one continuous utterance by the same speaker.
            - Output exactly one result block for each input speaker_block_id.
            - Do not output content before the first BEGIN_BLOCK line or after the final END_BLOCK line.
            - Do not output text from any previous or later chunk.
            - Within a speaker block, add Japanese punctuation and split sentences at natural boundaries.
            - Do not keep source segment line breaks when they make the prose choppy.
            - Preserve the meaning and intent of each speaker.
            - Add punctuation and paragraph breaks.
            - Remove fillers, repeated words, and obvious self-corrections only when they do not affect meaning.
            - Do not add facts that are not present in the transcript.
            - Do not guess uncertain names, numbers, dates, prices, decisions, owners, or deadlines.
            - Keep uncertain content cautious instead of making it confident.
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
