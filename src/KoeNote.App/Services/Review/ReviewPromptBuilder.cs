using KoeNote.App.Models;
using KoeNote.App.Services.Presets;

namespace KoeNote.App.Services.Review;

public sealed class ReviewPromptBuilder(ReviewGuidelineRepository? guidelineRepository = null)
{
    public string Build(IReadOnlyList<TranscriptSegment> segments)
    {
        var lines = segments.Select(segment =>
            $"- segment_id: {segment.SegmentId}\n  speaker: {segment.SpeakerId ?? ""}\n  text: {segment.NormalizedText ?? segment.RawText}");
        var guidelineBlock = BuildGuidelineBlock(guidelineRepository?.LoadEnabled() ?? []);

        return $$"""
            あなたは日本語ASR結果の校正レビュー担当です。原文を書き換えすぎず、意味の通る範囲で明らかな誤認識だけを最小限修正してください。

            制約:
            - 原文にない情報を追加しない
            - 意味が通る文は変更しない
            - 不確実な場合も候補として出し、confidence を低くする
            - 出力はJSONのみ。説明文、Markdown、コードフェンスは禁止
            - 候補がない場合は [] を返す
            {{guidelineBlock}}

            出力形式:
            [
              {
                "segment_id": "000012",
                "issue_type": "意味不明語の疑い",
                "original_text": "この仕様はサーバーのミギワで処理します",
                "suggested_text": "この仕様はサーバーの右側で処理します",
                "reason": "文脈上「ミギワ」が不自然で、音の近い語として「右側」が候補になります",
                "confidence": 0.62
              }
            ]

            ASRセグメント:
            {{string.Join("\n", lines)}}
            """;
    }

    public string BuildRepairPrompt(string invalidOutput)
    {
        return $$"""
            次の出力を、説明文なしのJSON配列だけに修復してください。
            CorrectionDraft の配列以外は出力しないでください。
            必須キー: segment_id, issue_type, original_text, suggested_text, reason, confidence

            修復対象:
            {{invalidOutput}}
            """;
    }

    private static string BuildGuidelineBlock(IReadOnlyList<string> guidelines)
    {
        var normalized = guidelines
            .Select(static guideline => guideline.Trim())
            .Where(static guideline => guideline.Length > 0)
            .Take(20)
            .ToArray();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return Environment.NewLine +
            Environment.NewLine +
            "専門領域レビュー指示:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, normalized.Select(static guideline => $"- {guideline}"));
    }
}
