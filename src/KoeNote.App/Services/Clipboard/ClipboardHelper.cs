using System.Runtime.InteropServices;
using System.Windows;

namespace KoeNote.App.Services.Clipboard;

public sealed record ClipboardSetTextResult(bool IsSucceeded, int Attempts, Exception? Exception)
{
    public string FailureMessage =>
        Exception is null
            ? "クリップボードへのコピーに失敗しました。"
            : $"クリップボードへのコピーに失敗しました。他のアプリがクリップボードを使用中の可能性があります: {Exception.Message}";
}

public static class ClipboardHelper
{
    private const int DefaultMaxAttempts = 3;
    private const int DefaultRetryDelayMilliseconds = 10;

    public static ClipboardSetTextResult TrySetText(string text)
    {
        return TrySetText(
            text,
            System.Windows.Clipboard.SetText,
            Thread.Sleep,
            DefaultMaxAttempts,
            DefaultRetryDelayMilliseconds);
    }

    internal static ClipboardSetTextResult TrySetText(
        string text,
        Action<string> setText,
        Action<int> delay,
        int maxAttempts,
        int retryDelayMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(setText);
        ArgumentNullException.ThrowIfNull(delay);
        if (maxAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "At least one attempt is required.");
        }

        if (retryDelayMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retryDelayMilliseconds), "Retry delay must be non-negative.");
        }

        Exception? lastException = null;
        var safeText = text ?? string.Empty;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                setText(safeText);
                return new ClipboardSetTextResult(true, attempt, null);
            }
            catch (Exception exception) when (IsClipboardBusyException(exception))
            {
                lastException = exception;
                if (attempt < maxAttempts)
                {
                    delay(retryDelayMilliseconds);
                }
            }
        }

        return new ClipboardSetTextResult(false, maxAttempts, lastException);
    }

    private static bool IsClipboardBusyException(Exception exception)
    {
        return exception is ExternalException or ThreadStateException;
    }
}
