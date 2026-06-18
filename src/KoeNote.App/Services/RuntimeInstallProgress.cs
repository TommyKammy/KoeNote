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
