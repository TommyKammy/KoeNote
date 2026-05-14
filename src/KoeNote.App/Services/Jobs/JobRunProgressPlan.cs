namespace KoeNote.App.Services.Jobs;

public static class JobRunProgressPlan
{
    public const int PreprocessRunning = 10;
    public const int PreprocessSucceeded = 15;
    public const int AsrRunning = 35;
    public const int AsrSucceeded = 55;
    public const int DiarizationRunning = 60;
    public const int DiarizationSucceeded = 65;
    public const int ReviewRunning = 72;
    public const int ReviewSucceeded = 82;
    public const int ReadablePolishingRunning = 92;
    public const int ReadablePolishingFailed = 95;
    // Summary is a separate post-process stage. Keep semantic names even when values match nearby stages.
    public const int SummaryRunning = 92;
    public const int SummaryFailed = 96;
    public const int Completed = 100;
}
