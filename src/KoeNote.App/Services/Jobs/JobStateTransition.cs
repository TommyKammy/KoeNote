namespace KoeNote.App.Services.Jobs;

internal sealed record JobStateTransition(
    string Status,
    string CurrentStage,
    int ProgressPercent)
{
    public JobStateTransition WithStatus(string status)
    {
        return this with { Status = status };
    }

    public JobStateTransition WithCurrentStage(string currentStage)
    {
        return this with { CurrentStage = currentStage };
    }

    public JobStateTransition WithProgressPercent(int progressPercent)
    {
        return this with { ProgressPercent = progressPercent };
    }
}
