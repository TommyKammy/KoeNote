using System.Text;

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
        var source = new StringBuilder();
        foreach (var block in BuildSpeakerBlocks(chunk.Segments))
        {
            source
                .Append("- speaker_block_id: ").Append(block.BlockId).AppendLine()
                .Append("  source_segment_ids: ").Append(string.Join(",", block.Segments.Select(static segment => segment.SegmentId))).AppendLine()
                .Append("  timestamp: ").Append(FormatTimestamp(block.StartSeconds)).Append(" - ").Append(FormatTimestamp(block.EndSeconds)).AppendLine()
                .Append("  start_seconds: ").Append(block.StartSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).AppendLine()
                .Append("  end_seconds: ").Append(block.EndSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)).AppendLine()
                .Append("  speaker: ").Append(block.Speaker).AppendLine()
                .AppendLine("  combined_text: |");
            foreach (var line in BuildCombinedTextLines(block.Segments))
            {
                source.Append("    ").AppendLine(line);
            }
        }

        if (string.Equals(promptTemplateId, BonsaiCompactPromptTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return BuildBonsaiCompactPrompt(source.ToString());
        }

        if (string.Equals(promptTemplateId, LlmJpPromptTemplateId, StringComparison.OrdinalIgnoreCase))
        {
            return BuildLlmJpPrompt(source.ToString());
        }

        return BuildGemmaBlockPrompt(source.ToString());
    }

    private static string BuildGemmaBlockPrompt(string source)
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

    private static string BuildBonsaiCompactPrompt(string source)
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

    private static string BuildLlmJpPrompt(string source)
    {
        return $$"""
            あなたは日本語の音声認識結果を読みやすく整える編集者です。

            指示:
            - 下の speaker_block だけを処理してください。
            - 入力された speaker_block_id ごとに、同じ順序で1つずつ出力してください。
            - タイムスタンプと話者ラベルは保持してください。
            - 日本語の句読点を補い、同じ話者内の不自然な改行を自然につなげてください。
            - 意味を変えないでください。要約や大幅な言い換えはしないでください。
            - 入力にない事実、名前、数字、日付、判断を追加しないでください。
            - 同じ文を繰り返さないでください。
            - 説明、見出し、Markdown、JSON、source metadata、Output: は出力しないでください。
            - BEGIN_BLOCK より前、END_BLOCK より後には何も出力しないでください。

            出力形式:
            BEGIN_BLOCK block-001
            [HH:MM:SS - HH:MM:SS] Speaker: 整えた本文
            END_BLOCK block-001

            Speaker blocks:
            {{source}}
            """;
    }

    private static IReadOnlyList<SpeakerBlock> BuildSpeakerBlocks(IReadOnlyList<TranscriptReadModel> segments)
    {
        if (segments.Count == 0)
        {
            return [];
        }

        var blocks = new List<SpeakerBlock>();
        var current = new List<TranscriptReadModel>();
        var currentSpeaker = string.Empty;

        foreach (var segment in segments)
        {
            if (current.Count > 0 && !string.Equals(currentSpeaker, segment.Speaker, StringComparison.Ordinal))
            {
                blocks.Add(CreateSpeakerBlock(blocks.Count + 1, current));
                current = [];
            }

            currentSpeaker = segment.Speaker;
            current.Add(segment);
        }

        if (current.Count > 0)
        {
            blocks.Add(CreateSpeakerBlock(blocks.Count + 1, current));
        }

        return blocks;
    }

    private static SpeakerBlock CreateSpeakerBlock(int index, IReadOnlyList<TranscriptReadModel> segments)
    {
        return new SpeakerBlock(
            $"block-{index:D3}",
            segments[0].Speaker,
            segments[0].StartSeconds,
            segments[^1].EndSeconds,
            segments);
    }

    private static IEnumerable<string> BuildCombinedTextLines(IReadOnlyList<TranscriptReadModel> segments)
    {
        return segments
            .Select(static segment => segment.Text.Trim())
            .Where(static text => text.Length > 0);
    }

    private static string FormatTimestamp(double seconds)
    {
        var clamped = Math.Max(0, seconds);
        var time = TimeSpan.FromSeconds(clamped);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes:00}:{time.Seconds:00}";
    }

    private sealed record SpeakerBlock(
        string BlockId,
        string Speaker,
        double StartSeconds,
        double EndSeconds,
        IReadOnlyList<TranscriptReadModel> Segments);
}
