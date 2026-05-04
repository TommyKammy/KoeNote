namespace KoeNote.App.Services.Setup;

internal static class SetupStepBuilder
{
    public static IReadOnlyList<SetupStepItem> Build(SetupState state)
    {
        return Enum.GetValues<SetupStep>()
            .Select(step => new SetupStepItem(step, GetStepTitle(step), GetStepStatus(step, state)))
            .ToArray();
    }

    private static string GetStepTitle(SetupStep step)
    {
        return step switch
        {
            SetupStep.Welcome => "Welcome",
            SetupStep.EnvironmentCheck => "Environment check",
            SetupStep.SetupMode => "Setup mode",
            SetupStep.AsrModel => "ASR model",
            SetupStep.ReviewModel => "Review LLM",
            SetupStep.Storage => "Storage",
            SetupStep.License => "License",
            SetupStep.Install => "Install/import",
            SetupStep.SmokeTest => "Smoke test",
            SetupStep.Complete => "Complete",
            _ => step.ToString()
        };
    }

    private static string GetStepStatus(SetupStep step, SetupState state)
    {
        if (state.IsCompleted)
        {
            return "done";
        }

        if (step == state.CurrentStep)
        {
            return "current";
        }

        return step < state.CurrentStep ? "done" : "pending";
    }
}
