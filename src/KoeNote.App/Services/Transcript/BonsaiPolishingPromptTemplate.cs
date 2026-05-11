namespace KoeNote.App.Services.Transcript;

internal static class BonsaiPolishingPromptTemplate
{
    public static string Build(string source)
    {
        return $$"""
            You are editing a short Japanese ASR transcript chunk.

            Rules:
            - Process only the speaker blocks shown below.
            - Output one block for each input speaker_block_id, in the same order.
            - Keep the timestamp and speaker label.
            - Add Japanese punctuation.
            - Join obviously broken line breaks inside the same speaker block.
            - Keep wording close to the input. Do not summarize, expand, or restructure.
            - Do not add new facts or repeat the same sentence.
            - Do not output explanations, headings, Markdown, JSON, source metadata, or an "Output:" label.
            - Do not output content before BEGIN_BLOCK or after END_BLOCK.

            Format:
            BEGIN_BLOCK block-001
            [HH:MM:SS - HH:MM:SS] Speaker: edited utterance
            END_BLOCK block-001

            Speaker blocks:
            {{source}}
            """;
    }
}
