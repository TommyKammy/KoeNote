namespace KoeNote.App.Services.Transcript;

public static class ReadablePolishingPromptPresetInstructions
{
    public static string Resolve(ReadablePolishingPromptSettings settings)
    {
        var normalized = settings.Normalize();
        if (normalized.UseCustomPrompt)
        {
            return string.Empty;
        }

        return normalized.PresetId switch
        {
            ReadablePolishingPromptPresets.StrongPunctuation => ResolveStrongPunctuation(normalized.ModelFamily),
            ReadablePolishingPromptPresets.Faithful => ResolveFaithful(normalized.ModelFamily),
            ReadablePolishingPromptPresets.MeetingMinutes => ResolveMeetingMinutes(normalized.ModelFamily),
            ReadablePolishingPromptPresets.LectureSeminar => ResolveLectureSeminar(normalized.ModelFamily),
            _ => string.Empty
        };
    }

    private static string ResolveStrongPunctuation(string modelFamily)
    {
        return modelFamily switch
        {
            ReadablePolishingPromptModelFamilies.Bonsai => """
                Add Japanese punctuation more actively than the standard setting.
                Join short consecutive lines inside the same speaker block into natural sentences.
                Do not leave the output as one line per source segment when the speaker is continuing one thought.
                Keep the wording close to the input and do not add new facts.
                """,
            ReadablePolishingPromptModelFamilies.LlmJp => """
                日本語の句読点を標準より積極的に補ってください。
                同じ話者ブロック内の短い行の連続は、自然な文として結合してください。
                話が続いている場合、入力セグメントごとの改行をそのまま残さないでください。
                意味を変えず、入力にない事実は追加しないでください。
                """,
            _ => """
                Add Japanese punctuation more actively than the standard setting.
                Merge choppy consecutive source lines inside the same speaker block into natural sentences.
                Do not preserve source line breaks when they are only ASR segmentation artifacts.
                Keep the meaning faithful and do not add new facts.
                """
        };
    }

    private static string ResolveFaithful(string modelFamily)
    {
        return modelFamily switch
        {
            ReadablePolishingPromptModelFamilies.LlmJp => """
                入力の語順と表現をできるだけ保ってください。
                言い換えは最小限にし、句読点と明らかな読みやすさの補正を中心にしてください。
                要約、補足、推測、話題の再構成はしないでください。
                """,
            _ => """
                Stay very close to the input wording and order.
                Prefer punctuation, light cleanup, and natural sentence boundaries over paraphrasing.
                Do not summarize, expand, infer, or restructure the speaker's argument.
                """
        };
    }

    private static string ResolveMeetingMinutes(string modelFamily)
    {
        return modelFamily switch
        {
            ReadablePolishingPromptModelFamilies.Bonsai => """
                Polish as a readable meeting transcript, not as a summary.
                Keep decisions, action items, names, dates, and numbers exactly as spoken.
                If a decision or owner is unclear, keep the wording cautious instead of inventing it.
                """,
            ReadablePolishingPromptModelFamilies.LlmJp => """
                会議録として読みやすい発話文に整えてください。ただし要約にはしないでください。
                決定事項、担当者、日付、数値は発話どおりに扱ってください。
                不明確な決定や担当者を推測して補わないでください。
                """,
            _ => """
                Polish as a readable meeting transcript, not as a summary.
                Preserve decisions, action items, names, dates, and numbers exactly as present in the transcript.
                Keep unclear decisions or owners cautious instead of making them definite.
                """
        };
    }

    private static string ResolveLectureSeminar(string modelFamily)
    {
        return modelFamily switch
        {
            ReadablePolishingPromptModelFamilies.Bonsai => """
                Polish as a readable lecture or seminar transcript.
                Join fragmented explanatory lines into complete sentences when they belong to the same speaker block.
                Preserve the speaker's terminology and do not add explanations that were not spoken.
                """,
            ReadablePolishingPromptModelFamilies.LlmJp => """
                講義やセミナーの書き起こしとして読みやすく整えてください。
                同じ話者ブロック内で説明が続いている短い行は、自然な文に結合してください。
                話者の用語を保ち、発話にない解説は追加しないでください。
                """,
            _ => """
                Polish as a readable lecture or seminar transcript.
                Join fragmented explanatory lines into complete sentences when they belong to the same speaker block.
                Preserve the speaker's terminology and do not add explanations that were not spoken.
                """
        };
    }
}
