using System.Runtime.InteropServices;
using KoeNote.App.Services.Clipboard;

namespace KoeNote.App.Tests;

public sealed class ClipboardHelperTests
{
    [Fact]
    public void TrySetText_RetriesClipboardBusyExceptionsAndSucceeds()
    {
        var attempts = 0;
        var delays = new List<int>();
        var copiedText = string.Empty;

        var result = ClipboardHelper.TrySetText(
            "hello",
            text =>
            {
                attempts++;
                if (attempts < 3)
                {
                    throw new COMException("clipboard busy", unchecked((int)0x800401D0));
                }

                copiedText = text;
            },
            delays.Add,
            maxAttempts: 3,
            retryDelayMilliseconds: 10);

        Assert.True(result.IsSucceeded);
        Assert.Equal(3, result.Attempts);
        Assert.Null(result.Exception);
        Assert.Equal("hello", copiedText);
        Assert.Equal([10, 10], delays);
    }

    [Fact]
    public void TrySetText_ReturnsFailureAfterClipboardBusyRetriesAreExhausted()
    {
        var delays = new List<int>();

        var result = ClipboardHelper.TrySetText(
            "hello",
            _ => throw new ExternalException("clipboard locked"),
            delays.Add,
            maxAttempts: 2,
            retryDelayMilliseconds: 5);

        Assert.False(result.IsSucceeded);
        Assert.Equal(2, result.Attempts);
        Assert.IsType<ExternalException>(result.Exception);
        Assert.Contains("clipboard locked", result.FailureMessage, StringComparison.Ordinal);
        Assert.Equal([5], delays);
    }

    [Fact]
    public void TrySetText_DoesNotSwallowUnexpectedExceptions()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ClipboardHelper.TrySetText(
                "hello",
                _ => throw new InvalidOperationException("unexpected"),
                _ => { },
                maxAttempts: 3,
                retryDelayMilliseconds: 10));

        Assert.Equal("unexpected", exception.Message);
    }
}
