using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class SetupWizardPresentationInvalidatorTests
{
    [Fact]
    public void RaisePresentationChanged_GroupsCoreSetupNotificationsWithoutDuplicates()
    {
        var properties = Capture(SetupWizardPresentationInvalidator.RaisePresentationChanged);

        AssertNoDuplicates(properties);
        Assert.Contains(nameof(MainWindowViewModel.SelectedSetupAsrModel), properties);
        Assert.Contains(nameof(MainWindowViewModel.SelectedSettingsReviewModelId), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupRequiredRuntimeReady), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupPrimaryInstallActionText), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupCurrentStep), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupLicenseAccepted), properties);
    }

    [Fact]
    public void RaiseRunPreflightChanged_GroupsRunReadinessNotificationsWithoutDuplicates()
    {
        var properties = Capture(SetupWizardPresentationInvalidator.RaiseRunPreflightChanged);

        AssertNoDuplicates(properties);
        Assert.Equal(
            [
                nameof(MainWindowViewModel.IsSetupComplete),
                nameof(MainWindowViewModel.RequiredRuntimeAssetsReady),
                nameof(MainWindowViewModel.ReviewStageAssetsReady),
                nameof(MainWindowViewModel.SummaryStageAssetsReady),
                nameof(MainWindowViewModel.CanRunSelectedJob),
                nameof(MainWindowViewModel.RunPreflightSummary),
                nameof(MainWindowViewModel.RunPreflightDetail)
            ],
            properties);
    }

    [Fact]
    public void RaiseSelectionPreviewChanged_KeepsTransientSelectionNotificationsFocused()
    {
        var properties = Capture(SetupWizardPresentationInvalidator.RaiseSelectionPreviewChanged);

        AssertNoDuplicates(properties);
        Assert.Contains(nameof(MainWindowViewModel.SelectedSetupModelPreset), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupMode), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupStorageRoot), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupReviewRuntimeReady), properties);
        Assert.Contains(nameof(MainWindowViewModel.SetupNextActionText), properties);
        Assert.DoesNotContain(nameof(MainWindowViewModel.AsrModel), properties);
    }

    private static List<string> Capture(Action<Action<string>> raise)
    {
        var properties = new List<string>();
        raise(properties.Add);
        return properties;
    }

    private static void AssertNoDuplicates(IReadOnlyCollection<string> properties)
    {
        Assert.Equal(properties.Count, properties.Distinct(StringComparer.Ordinal).Count());
    }
}
