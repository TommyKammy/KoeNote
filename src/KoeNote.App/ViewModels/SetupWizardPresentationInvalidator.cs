namespace KoeNote.App.ViewModels;

internal static class SetupWizardPresentationInvalidator
{
    public static void RaisePresentationChanged(Action<string> notify)
    {
        RaiseSelectedModelChanged(notify);
        RaiseRuntimeReadinessChanged(notify);
        RaiseSetupInstallActionChanged(notify);
        RaiseWizardNavigationChanged(notify);
    }

    public static void RaiseSelectionPreviewChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SelectedSetupAsrModel),
            nameof(MainWindowViewModel.SelectedSetupReviewModel),
            nameof(MainWindowViewModel.SelectedSettingsReviewModel),
            nameof(MainWindowViewModel.SelectedSettingsReviewModelId),
            nameof(MainWindowViewModel.SelectedSetupModelPreset),
            nameof(MainWindowViewModel.SelectedSetupModelPresetDescription),
            nameof(MainWindowViewModel.SelectedSetupModelPresetModels),
            nameof(MainWindowViewModel.SelectedSetupModelsReady),
            nameof(MainWindowViewModel.SelectedSetupGemma12BMtpDraftReady),
            nameof(MainWindowViewModel.SetupMode),
            nameof(MainWindowViewModel.SetupStorageRoot));
        RaiseReviewRuntimeReadinessChanged(notify);
        RaiseSetupInstallActionChanged(notify);
    }

    public static void RaiseRuntimeSummaryChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SetupDiarizationRuntimeSummary),
            nameof(MainWindowViewModel.SetupAsrCudaRuntimeSummary),
            nameof(MainWindowViewModel.SetupCudaReviewRuntimeSummary));
    }

    public static void RaiseRunPreflightChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.IsSetupComplete),
            nameof(MainWindowViewModel.RequiredRuntimeAssetsReady),
            nameof(MainWindowViewModel.ReviewStageAssetsReady),
            nameof(MainWindowViewModel.SummaryStageAssetsReady),
            nameof(MainWindowViewModel.CanRunSelectedJob),
            nameof(MainWindowViewModel.RunPreflightSummary),
            nameof(MainWindowViewModel.RunPreflightDetail));
    }

    private static void RaiseSelectedModelChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SelectedSetupAsrModel),
            nameof(MainWindowViewModel.SelectedSetupReviewModel),
            nameof(MainWindowViewModel.SelectedSettingsReviewModel),
            nameof(MainWindowViewModel.SelectedSettingsReviewModelId),
            nameof(MainWindowViewModel.SelectedSetupModelPreset),
            nameof(MainWindowViewModel.AsrModel),
            nameof(MainWindowViewModel.ReviewModel),
            nameof(MainWindowViewModel.SelectedSetupModelPresetDescription),
            nameof(MainWindowViewModel.SelectedSetupModelPresetModels),
            nameof(MainWindowViewModel.SelectedSetupModelsReady),
            nameof(MainWindowViewModel.SelectedSetupGemma12BMtpDraftReady));
    }

    private static void RaiseRuntimeReadinessChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SetupFasterWhisperRuntimeReady),
            nameof(MainWindowViewModel.SetupReviewRuntimeReady),
            nameof(MainWindowViewModel.SetupDiarizationRuntimeReady),
            nameof(MainWindowViewModel.SetupAsrCudaRuntimeRecommended),
            nameof(MainWindowViewModel.SetupAsrCudaRuntimeReady),
            nameof(MainWindowViewModel.SetupAsrCudaRuntimeActionText),
            nameof(MainWindowViewModel.SetupCudaReviewRuntimeRecommended),
            nameof(MainWindowViewModel.SetupCudaReviewRuntimeReady),
            nameof(MainWindowViewModel.SetupCudaReviewRuntimeActionText),
            nameof(MainWindowViewModel.SetupTernaryReviewRuntimeReady),
            nameof(MainWindowViewModel.SetupRequiredRuntimeReady),
            nameof(MainWindowViewModel.SelectedSetupGpuRequirementSatisfied),
            nameof(MainWindowViewModel.SetupGpuRuntimeRequiredButMissing),
            nameof(MainWindowViewModel.SetupConditionalRuntimeReady),
            nameof(MainWindowViewModel.SelectedSetupConfigurationReady));
    }

    private static void RaiseReviewRuntimeReadinessChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SetupReviewRuntimeReady),
            nameof(MainWindowViewModel.SetupTernaryReviewRuntimeReady),
            nameof(MainWindowViewModel.SetupRequiredRuntimeReady),
            nameof(MainWindowViewModel.SelectedSetupGpuRequirementSatisfied),
            nameof(MainWindowViewModel.SetupGpuRuntimeRequiredButMissing),
            nameof(MainWindowViewModel.SetupConditionalRuntimeReady),
            nameof(MainWindowViewModel.SelectedSetupConfigurationReady));
    }

    private static void RaiseSetupInstallActionChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SetupPrimaryInstallActionText),
            nameof(MainWindowViewModel.SetupPrimaryInstallSummary),
            nameof(MainWindowViewModel.SetupNextActionText),
            nameof(MainWindowViewModel.ShowSetupInlineInstallAction),
            nameof(MainWindowViewModel.ShowSetupLicenseNotice),
            nameof(MainWindowViewModel.ShowSetupSmokeAction),
            nameof(MainWindowViewModel.ShowSetupCompleteAction),
            nameof(MainWindowViewModel.SetupLicenseNoticeText),
            nameof(MainWindowViewModel.SetupPresetRecommendationSummary),
            nameof(MainWindowViewModel.SetupPresetRecommendationDetail));
    }

    private static void RaiseWizardNavigationChanged(Action<string> notify)
    {
        Notify(
            notify,
            nameof(MainWindowViewModel.SetupCurrentStep),
            nameof(MainWindowViewModel.SetupStepDisplayName),
            nameof(MainWindowViewModel.SetupStatusSummary),
            nameof(MainWindowViewModel.SetupCompleteActionText),
            nameof(MainWindowViewModel.SetupWizardModalTitle),
            nameof(MainWindowViewModel.SetupWizardModalGuide),
            nameof(MainWindowViewModel.SetupMode),
            nameof(MainWindowViewModel.SetupStorageRoot),
            nameof(MainWindowViewModel.SetupLicenseAccepted));
    }

    private static void Notify(Action<string> notify, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            notify(propertyName);
        }
    }
}
