using KoeNote.App.Services.Clipboard;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public void ReportClipboardCopyFailure(string targetName, ClipboardSetTextResult result)
    {
        LatestLog = BuildClipboardCopyFailureMessage(targetName, result);
    }

    private static string BuildClipboardCopyFailureMessage(string targetName, ClipboardSetTextResult result)
    {
        var detail = result.Exception is null || string.IsNullOrWhiteSpace(result.Exception.Message)
            ? "他のアプリがクリップボードを使用中の可能性があります。"
            : $"他のアプリがクリップボードを使用中の可能性があります: {result.Exception.Message}";
        return $"{targetName}のコピーに失敗しました。{detail}";
    }
}
