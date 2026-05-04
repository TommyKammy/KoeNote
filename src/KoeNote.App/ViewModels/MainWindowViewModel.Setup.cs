using KoeNote.App.Services.Setup;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task SetupBackAsync()
    {
        _setupState = _setupWizardService.MoveBack();
        RefreshSetupWizard();
        LatestLog = $"Setup moved to {_setupState.CurrentStep}.";
        return Task.CompletedTask;
    }

    private Task SetupNextAsync()
    {
        _setupState = _setupWizardService.MoveNext();
        RefreshSetupWizard();
        LatestLog = $"Setup moved to {_setupState.CurrentStep}.";
        return Task.CompletedTask;
    }

    private Task SetupUseRecommendedAsync()
    {
        _setupState = _setupWizardService.UseRecommendedSelections();
        RefreshSetupWizard();
        LatestLog = "Recommended setup choices selected. Review ASR and LLM choices, then accept licenses.";
        return Task.CompletedTask;
    }

    private Task SetupAcceptLicensesAsync()
    {
        _setupState = _setupWizardService.AcceptLicenses();
        RefreshSetupWizard();
        LatestLog = "Setup licenses accepted. Install or import the selected models, then run smoke check.";
        return Task.CompletedTask;
    }

    private async Task SetupDownloadAsrAsync()
    {
        var result = await _setupWizardService.DownloadSelectedModelAsync("asr");
        RefreshSetupWizard();
        LatestLog = result.Message;
    }

    private async Task SetupDownloadReviewAsync()
    {
        var result = await _setupWizardService.DownloadSelectedModelAsync("review");
        RefreshSetupWizard();
        LatestLog = result.Message;
    }

    private Task SetupRegisterLocalAsrAsync()
    {
        var result = _setupWizardService.RegisterSelectedLocalModel("asr", SetupLocalModelPath);
        RefreshSetupWizard();
        LatestLog = result.Message;
        return Task.CompletedTask;
    }

    private Task SetupRegisterLocalReviewAsync()
    {
        var result = _setupWizardService.RegisterSelectedLocalModel("review", SetupLocalModelPath);
        RefreshSetupWizard();
        LatestLog = result.Message;
        return Task.CompletedTask;
    }

    private Task SetupImportOfflinePackAsync()
    {
        var result = _setupWizardService.ImportOfflineModelPack(SetupOfflineModelPackPath);
        RefreshSetupWizard();
        LatestLog = result.Message;
        return Task.CompletedTask;
    }

    private Task SetupRunSmokeAsync()
    {
        var result = _setupWizardService.RunSmokeCheck();
        SetupSmokeChecks.Clear();
        foreach (var check in result.Checks)
        {
            SetupSmokeChecks.Add(check);
        }

        _setupState = _setupWizardService.LoadState();
        RefreshSetupWizard(refreshSmokeChecks: false);
        LatestLog = result.IsSucceeded
            ? $"Setup smoke check passed. Report: {result.ReportPath}"
            : $"Setup smoke check found missing items. Report: {result.ReportPath}";
        return Task.CompletedTask;
    }

    private Task SetupCompleteAsync()
    {
        _setupState = _setupWizardService.CompleteIfReady();
        RefreshSetupWizard();
        LatestLog = _setupState.IsCompleted
            ? "Setup completed. Run is now available when runtime assets are ready."
            : "Setup is still incomplete. Run smoke check after installing/importing selected models.";
        return Task.CompletedTask;
    }

    private void ApplySetupModelSelection(string role, string modelId)
    {
        try
        {
            _setupState = _setupWizardService.SelectModel(role, modelId);
            if (role.Equals("asr", StringComparison.OrdinalIgnoreCase) && SelectedSetupAsrModel is not null)
            {
                SelectedAsrEngineId = SelectedSetupAsrModel.EngineId;
            }

            RefreshSetupWizard();
            LatestLog = $"Setup {role} model selected: {modelId}";
        }
        catch (InvalidOperationException ex)
        {
            LatestLog = $"Setup selection failed: {ex.Message}";
        }
    }

    private void RefreshSetupWizard(bool refreshSmokeChecks = true)
    {
        _setupState = _setupWizardService.LoadState();
        SetupSteps.Clear();
        foreach (var step in _setupWizardService.BuildStepItems(_setupState))
        {
            SetupSteps.Add(step);
        }

        SetupAsrModelChoices.Clear();
        foreach (var entry in _setupWizardService.GetSelectableModels("asr"))
        {
            SetupAsrModelChoices.Add(entry);
        }

        SetupReviewModelChoices.Clear();
        foreach (var entry in _setupWizardService.GetSelectableModels("review"))
        {
            SetupReviewModelChoices.Add(entry);
        }

        _selectedSetupAsrModel = SetupAsrModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(_setupState.SelectedAsrModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupAsrModelChoices.FirstOrDefault();
        _selectedSetupReviewModel = SetupReviewModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(_setupState.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupReviewModelChoices.FirstOrDefault();

        if (refreshSmokeChecks)
        {
            SetupSmokeChecks.Clear();
            foreach (var check in _setupWizardService.GetEnvironmentChecks()
                .Select(static check => new SetupSmokeCheck(check.Name, check.IsOk, check.Detail)))
            {
                SetupSmokeChecks.Add(check);
            }
        }

        OnPropertyChanged(nameof(SelectedSetupAsrModel));
        OnPropertyChanged(nameof(SelectedSetupReviewModel));
        OnPropertyChanged(nameof(SetupCurrentStep));
        OnPropertyChanged(nameof(SetupStatusSummary));
        OnPropertyChanged(nameof(SetupMode));
        OnPropertyChanged(nameof(SetupStorageRoot));
        OnPropertyChanged(nameof(SetupLicenseAccepted));
        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(CanRunSelectedJob));
        SetupModelAudits.Clear();
        foreach (var audit in _setupWizardService.GetSelectedModelAudit())
        {
            SetupModelAudits.Add(audit);
        }

        if (RunSelectedJobCommand is RelayCommand runCommand)
        {
            runCommand.RaiseCanExecuteChanged();
        }
    }
}
