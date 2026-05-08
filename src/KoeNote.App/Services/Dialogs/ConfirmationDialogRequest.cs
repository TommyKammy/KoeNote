namespace KoeNote.App.Services.Dialogs;

public sealed record ConfirmationDialogRequest(
    string Title,
    string Heading,
    string Message,
    string ConfirmText,
    string CancelText,
    string IconGlyph,
    string IconBackground,
    string IconForeground,
    string ConfirmBackground,
    string ConfirmBorderBrush,
    string ConfirmForeground)
{
    public static ConfirmationDialogRequest Warning(
        string title,
        string message,
        string confirmText = "続行")
    {
        return new ConfirmationDialogRequest(
            title,
            title,
            message,
            confirmText,
            "キャンセル",
            "\uE7BA",
            "#FEF3C7",
            "#B45309",
            "#FEF2F2",
            "#FECACA",
            "#B91C1C");
    }

    public static ConfirmationDialogRequest Exit(bool isRunInProgress)
    {
        return new ConfirmationDialogRequest(
            "終了の確認",
            "KoeNote を終了しますか？",
            isRunInProgress
                ? "処理が実行中です。終了すると現在の処理は中断されます。"
                : "開いている作業を閉じて、アプリを終了します。",
            "終了",
            "キャンセル",
            isRunInProgress ? "\uE7BA" : "\uE8BB",
            isRunInProgress ? "#FEF3C7" : "#ECFDF5",
            isRunInProgress ? "#B45309" : "#15803D",
            "#FEF2F2",
            "#FECACA",
            "#B91C1C");
    }
}
