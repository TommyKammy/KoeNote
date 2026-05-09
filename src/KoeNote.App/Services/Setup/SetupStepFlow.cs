namespace KoeNote.App.Services.Setup;

internal static class SetupStepFlow
{
    public static readonly SetupStep[] OrderedSteps =
    [
        SetupStep.SetupMode,
        SetupStep.InstallPlan,
        SetupStep.Install,
        SetupStep.SmokeTest,
        SetupStep.Complete
    ];

    public static SetupStep GetNext(SetupStep current)
    {
        var index = GetIndex(current);
        return index >= OrderedSteps.Length - 1
            ? SetupStep.Complete
            : OrderedSteps[index + 1];
    }

    public static SetupStep GetPrevious(SetupStep current)
    {
        var index = GetIndex(current);
        return index <= 0
            ? SetupStep.Welcome
            : OrderedSteps[index - 1];
    }

    public static bool HasReached(SetupStep current, SetupStep target)
    {
        return GetIndex(current) >= GetIndex(target);
    }

    public static bool IsAfter(SetupStep current, SetupStep target)
    {
        return GetIndex(current) > GetIndex(target);
    }

    private static int GetIndex(SetupStep step)
    {
        var index = Array.IndexOf(OrderedSteps, step);
        return index < 0 ? GetLegacyIndex(step) : index;
    }

    private static int GetLegacyIndex(SetupStep step)
    {
        return step switch
        {
            SetupStep.Welcome or SetupStep.EnvironmentCheck => Array.IndexOf(OrderedSteps, SetupStep.SetupMode),
            SetupStep.AsrModel or SetupStep.ReviewModel or SetupStep.Storage or SetupStep.License => Array.IndexOf(OrderedSteps, SetupStep.InstallPlan),
            _ => 0
        };
    }
}
