using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Tests;

public sealed class JobLogDiagnosticsTests
{
    [Fact]
    public void FormatException_IncludesCategoryTypeRelatedPathAndStack()
    {
        var exception = CreateException();

        var message = JobLogDiagnostics.FormatException("asr_failed", exception, @"C:\logs\asr");

        Assert.Contains("asr_failed: boom", message);
        Assert.Contains("exception_type: System.InvalidOperationException", message);
        Assert.Contains(@"related_path: C:\logs\asr", message);
        Assert.Contains("exception:", message);
        Assert.Contains(nameof(CreateException), message);
    }

    private static Exception CreateException()
    {
        try
        {
            throw new InvalidOperationException("boom");
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
