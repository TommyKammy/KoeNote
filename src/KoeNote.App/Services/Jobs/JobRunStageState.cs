namespace KoeNote.App.Services.Jobs;

public enum JobRunStageState
{
    Running,
    Succeeded,
    Skipped,
    Cancelled,
    Failed
}
