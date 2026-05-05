namespace KoeNote.App.Services.Setup;

internal static class SetupStepBuilder
{
    public static IReadOnlyList<SetupStepItem> Build(SetupState state, SetupStepReadiness readiness)
    {
        return Enum.GetValues<SetupStep>()
            .Select(step => new SetupStepItem(step, GetStepTitle(step), GetStepStatus(step, state, readiness)))
            .ToArray();
    }

    private static string GetStepTitle(SetupStep step)
    {
        return step switch
        {
            SetupStep.Welcome => "ようこそ",
            SetupStep.EnvironmentCheck => "環境確認",
            SetupStep.SetupMode => "セットアップ方式",
            SetupStep.AsrModel => "ASRモデル",
            SetupStep.ReviewModel => "推敲LLM",
            SetupStep.Storage => "保存先",
            SetupStep.License => "ライセンス",
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
            SetupStep.Welcome => state.CurrentStep > SetupStep.Welcome,
            SetupStep.EnvironmentCheck => readiness.EnvironmentReady,
            SetupStep.SetupMode => state.CurrentStep > SetupStep.SetupMode && !string.IsNullOrWhiteSpace(state.SetupMode),
            SetupStep.AsrModel => readiness.AsrModelReady,
            SetupStep.ReviewModel => readiness.ReviewModelReady,
            SetupStep.Storage => readiness.StorageReady,
            SetupStep.License => state.LicenseAccepted,
            SetupStep.Install => readiness.AsrModelReady && readiness.ReviewModelReady,
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
    bool StorageReady);
