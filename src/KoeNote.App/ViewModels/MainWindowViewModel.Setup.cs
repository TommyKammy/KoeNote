using System.IO;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;
using Microsoft.Win32;

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
        var displayName = SelectedSetupAsrModel?.DisplayName ?? "ASR model";
        BeginModelDownloadProgress(displayName);
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            RefreshModelCatalogKeepingSelection(downloadProgress.ModelId);
            UpdateModelDownloadProgress(displayName, downloadProgress);
        });
        var result = await _setupWizardService.DownloadSelectedModelAsync("asr", progress);
        RefreshSetupWizard();
        CompleteSetupModelDownload(displayName, result);
    }

    private async Task SetupDownloadReviewAsync()
    {
        var displayName = SelectedSetupReviewModel?.DisplayName ?? "Review LLM model";
        BeginModelDownloadProgress(displayName);
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            RefreshModelCatalogKeepingSelection(downloadProgress.ModelId);
            UpdateModelDownloadProgress(displayName, downloadProgress);
        });
        var result = await _setupWizardService.DownloadSelectedModelAsync("review", progress);
        RefreshSetupWizard();
        CompleteSetupModelDownload(displayName, result);
    }

    private void CompleteSetupModelDownload(string displayName, SetupInstallResult result)
    {
        if (result.IsSucceeded)
        {
            CompleteModelDownloadProgress(displayName, succeeded: true);
            if (result.Message.StartsWith("Already installed", StringComparison.OrdinalIgnoreCase))
            {
                ModelDownloadProgressSummary = result.Message;
                ModelDownloadNotification = result.Message;
                LatestLog = result.Message;
            }

            return;
        }

        CompleteModelDownloadProgress(displayName, succeeded: false, result.Message);
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

    private Task SetupChooseStorageRootAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "モデルの保存先フォルダを選択",
            InitialDirectory = Directory.Exists(SetupStorageRoot)
                ? SetupStorageRoot
                : Paths.DefaultModelStorageRoot
        };

        if (dialog.ShowDialog() != true)
        {
            return Task.CompletedTask;
        }

        Directory.CreateDirectory(dialog.FolderName);
        _setupState = _setupWizardService.SetStorageRoot(dialog.FolderName);
        RefreshSetupWizard();
        LatestLog = $"モデル保存先を変更しました: {dialog.FolderName}";
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
            ? $"最終確認に成功しました。Report: {result.ReportPath}"
            : $"最終確認で不足項目が見つかりました。Report: {result.ReportPath}";
        return Task.CompletedTask;
    }

    private Task SetupCompleteAsync()
    {
        var checkResult = _setupWizardService.RunSmokeCheck();
        SetupSmokeChecks.Clear();
        foreach (var check in checkResult.Checks)
        {
            SetupSmokeChecks.Add(check);
        }

        _setupState = _setupWizardService.CompleteIfReady();
        RefreshSetupWizard(refreshSmokeChecks: false);
        if (_setupState.IsCompleted)
        {
            IsSetupWizardModalOpen = false;
        }

        var firstMissing = checkResult.Checks.FirstOrDefault(static check => !check.IsOk);
        LatestLog = _setupState.IsCompleted
            ? "セットアップが完了しました。必要なモデルと実行環境が揃っていれば実行できます。"
            : firstMissing is null
                ? "セットアップはまだ完了していません。モデル導入とライセンス同意を確認してください。"
                : $"セットアップはまだ完了していません。不足項目: {firstMissing.Name} - {firstMissing.Detail}";
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
        OnPropertyChanged(nameof(SetupStepDisplayName));
        OnPropertyChanged(nameof(SetupStatusSummary));
        OnPropertyChanged(nameof(SetupWizardModalTitle));
        OnPropertyChanged(nameof(SetupWizardModalGuide));
        OnPropertyChanged(nameof(SetupMode));
        OnPropertyChanged(nameof(SetupStorageRoot));
        OnPropertyChanged(nameof(SetupLicenseAccepted));
        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(RequiredRuntimeAssetsReady));
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
