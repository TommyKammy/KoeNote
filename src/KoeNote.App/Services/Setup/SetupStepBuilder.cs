namespace KoeNote.App.Services.Setup;

internal static class SetupStepBuilder
{
    public static IReadOnlyList<SetupStepItem> Build(SetupState state, SetupStepReadiness readiness)
    {
        return SetupStepFlow.OrderedSteps
            .Select(step => new SetupStepItem(step, GetStepTitle(step), GetStepStatus(step, state, readiness)))
            .ToArray();
    }

    private static string GetStepTitle(SetupStep step)
    {
        return step switch
        {
            SetupStep.SetupMode => "プリセット選択",
            SetupStep.InstallPlan => "導入内容",
            SetupStep.Install => "モデル導入",
            SetupStep.SmokeTest => "最終確認",
            SetupStep.Complete => "完了",
            _ => step.ToString()
        };
    }

    private static string GetStepStatus(SetupStep step, SetupState state, SetupStepReadiness readiness)
    {
        if (IsStepReady(step, state, readiness))
        {
            return "\u2713";
        }

        if (step == state.CurrentStep)
        {
            return "\u25cf";
        }

        return string.Empty;
    }

    private static bool IsStepReady(SetupStep step, SetupState state, SetupStepReadiness readiness)
    {
        if (state.IsCompleted)
        {
            return true;
        }

        return step switch
        {
            SetupStep.SetupMode => SetupStepFlow.IsAfter(state.CurrentStep, SetupStep.SetupMode) &&
                !string.IsNullOrWhiteSpace(state.SelectedModelPresetId),
            SetupStep.InstallPlan => state.LicenseAccepted && SetupStepFlow.IsAfter(state.CurrentStep, SetupStep.InstallPlan),
            SetupStep.Install => readiness.AsrModelReady && readiness.ReviewModelReady && readiness.ReviewRuntimeReady,
            SetupStep.SmokeTest => state.LastSmokeSucceeded,
            SetupStep.Complete => state.IsCompleted,
            _ => false
        };
    }
}

internal sealed record SetupStepReadiness(
    bool EnvironmentReady,
    bool AsrModelReady,
    bool ReviewModelReady,
    bool ReviewRuntimeReady,
    bool StorageReady);
