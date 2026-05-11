namespace KoeNote.App.Services.Transcript;

internal static class LlmJpPolishingPromptTemplate
{
    public static string Build(string source)
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
}
