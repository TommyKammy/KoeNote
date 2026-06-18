namespace KoeNote.App.Services;

public sealed record RuntimeInstallProgress(
    string StageText,
    string Message,
    double? Percent = null,
    long? BytesDownloaded = null,
    long? BytesTotal = null,
    bool IsIndeterminate = true)
{
    public double? DisplayPercent
    {
        get
        {
            if (Percent is { } percent)
            {
                return Math.Clamp(percent, 0, 100);
            }

            return BytesDownloaded is >= 0 && BytesTotal is > 0 && BytesDownloaded <= BytesTotal.Value
                ? Math.Clamp(BytesDownloaded.Value * 100d / BytesTotal.Value, 0, 100)
                : null;
        }
    }
}

internal sealed class RuntimeInstallProgressReporter(IProgress<RuntimeInstallProgress>? progress)
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromSeconds(1);
    private const long ByteStep = 1024 * 1024;

    private DateTimeOffset _lastReportedAt = DateTimeOffset.MinValue;
    private long? _lastReportedBytes;
    private long? _lastReportedTotalBytes;
    private int? _lastReportedPercent;
    private string? _lastReportedStageText;
    private string? _lastReportedMessage;

    public void Report(RuntimeInstallProgress value, bool force = false)
    {
        if (progress is null)
        {
            return;
        }

        if (!force && !ShouldReport(value))
        {
            return;
        }

        _lastReportedAt = DateTimeOffset.UtcNow;
        _lastReportedBytes = value.BytesDownloaded;
        _lastReportedTotalBytes = value.BytesTotal;
        _lastReportedPercent = value.DisplayPercent is { } percent
            ? (int)Math.Floor(percent)
            : null;
        _lastReportedStageText = value.StageText;
        _lastReportedMessage = value.Message;
        progress.Report(value);
    }

    private bool ShouldReport(RuntimeInstallProgress value)
    {
        if (_lastReportedAt == DateTimeOffset.MinValue)
        {
            return true;
        }

        if (!string.Equals(_lastReportedStageText, value.StageText, StringComparison.Ordinal) ||
            !string.Equals(_lastReportedMessage, value.Message, StringComparison.Ordinal))
        {
            return true;
        }

        if (value.DisplayPercent is { } percent)
        {
            var visiblePercent = (int)Math.Floor(percent);
            if (visiblePercent != _lastReportedPercent)
            {
                return true;
            }
        }

        if (value.BytesTotal != _lastReportedTotalBytes)
        {
            return true;
        }

        if (value.BytesDownloaded is { } bytes &&
            (!_lastReportedBytes.HasValue || bytes - _lastReportedBytes.Value >= ByteStep))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - _lastReportedAt >= ReportInterval;
    }
}
