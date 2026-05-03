namespace KoeNote.App.Services;

public static class TimestampFormatter
{
    public static string FormatDisplay(double seconds)
    {
        return Format(seconds, ".");
    }

    public static string FormatSrt(double seconds)
    {
        return Format(seconds, ",");
    }

    public static string FormatVtt(double seconds)
    {
        return Format(seconds, ".");
    }

    private static string Format(double seconds, string millisecondSeparator)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}{millisecondSeparator}{time.Milliseconds:000}";
    }
}
