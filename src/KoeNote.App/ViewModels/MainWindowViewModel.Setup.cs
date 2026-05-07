using System.IO;
using KoeNote.App.Services.Diarization;
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
        SelectedAsrEngineId = _setupState.SelectedAsrModelId ?? SelectedAsrEngineId;
        RefreshSetupWizard();
        LatestLog = "Recommended setup choices selected. Review ASR and LLM choices, then accept licenses.";
        return Task.CompletedTask;
    }

    private void ApplySetupModelPreset(string presetId)
    {
        try
        {
            _setupState = _setupWizardService.SelectModelPreset(presetId);
            if (!string.IsNullOrWhiteSpace(_setupState.SelectedAsrModelId))
            {
                SelectedAsrEngineId = _setupState.SelectedAsrModelId;
            }

            RefreshSetupWizard();
            LatestLog = $"Setup model preset selected: {presetId}";
        }
        catch (InvalidOperationException ex)
        {
            LatestLog = $"Setup preset selection failed: {ex.Message}";
        }
    }

    private Task SetupAcceptLicensesAsync()
    {
        _setupState = _setupWizardService.AcceptLicenses();
        RefreshSetupWizard();
        LatestLog = "Setup licenses accepted. Install or import the selected models, then run smoke check.";
        return Task.CompletedTask;
    }

    private bool CanInstallSelectedPreset()
    {
        return !IsModelDownloadInProgress &&
            SelectedSetupAsrModel is not null &&
            SelectedSetupReviewModel is not null &&
            !SelectedSetupConfigurationReady;
    }

    private bool CanDownloadSetupAsr()
    {
        return !IsModelDownloadInProgress &&
            SelectedSetupAsrModel is not null &&
            !IsSetupModelReady(SelectedSetupAsrModel);
    }

    private bool CanDownloadSetupReview()
    {
        return !IsModelDownloadInProgress &&
            SelectedSetupReviewModel is not null &&
            !IsSetupModelReady(SelectedSetupReviewModel);
    }

    private async Task SetupInstallSelectedPresetAsync()
    {
        if (IsModelDownloadInProgress)
        {
            return;
        }

        var displayName = SelectedSetupModelPreset?.DisplayName ?? "selected model preset";
        BeginModelDownloadProgress(displayName);
        ModelDownloadProgressSummary = $"Installing {displayName}: checking ASR, Review, and speaker diarization runtime...";
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            var modelName = FindSetupModelDisplayName(
                SetupAsrModelChoices.Concat(SetupReviewModelChoices),
                downloadProgress.ModelId);
            UpdateModelDownloadProgress(modelName, downloadProgress);
            RefreshModelCatalogForDownloadProgress(downloadProgress);
        });

        try
        {
            var modelResult = await _setupWizardService.InstallSelectedPresetModelsAsync(progress);
            RefreshSetupWizard();
            if (!modelResult.IsSucceeded)
            {
                CompleteSetupModelDownload(displayName, modelResult);
                return;
            }

            if (!SetupDiarizationRuntimeReady)
            {
                ModelDownloadProgressSummary = $"Installing {displayName}: installing speaker diarization runtime...";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallDiarizationRuntimeAsync();
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    CompleteModelDownloadProgress(displayName, succeeded: false, runtimeResult.Message);
                    LatestLog = runtimeResult.Message;
                    return;
                }

                SetupDiarizationRuntimeSummary = $"話者識別ランタイム導入済み: {runtimeResult.InstallPath}";
                LatestLog = runtimeResult.Message;
            }

            CompleteModelDownloadProgress(displayName, succeeded: true);
        }
        catch (OperationCanceledException)
        {
            RefreshSetupWizard();
            CompleteModelDownloadProgress(displayName, succeeded: false, "Preset model installation was cancelled.");
            LatestLog = "Preset model installation was cancelled.";
        }
    }

    private async Task SetupDownloadAsrAsync()
    {
        var displayName = SelectedSetupAsrModel?.DisplayName ?? "ASR model";
        BeginModelDownloadProgress(displayName);
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            UpdateModelDownloadProgress(displayName, downloadProgress);
            RefreshModelCatalogForDownloadProgress(downloadProgress);
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
            UpdateModelDownloadProgress(displayName, downloadProgress);
            RefreshModelCatalogForDownloadProgress(downloadProgress);
        });
        var result = await _setupWizardService.DownloadSelectedModelAsync("review", progress);
        RefreshSetupWizard();
        CompleteSetupModelDownload(displayName, result);
    }

    private async Task SetupInstallDiarizationRuntimeAsync()
    {
        const string displayName = "diarize speaker diarization runtime";
        BeginModelDownloadProgress(displayName);
        ModelDownloadProgressSummary = "Installing diarize runtime with pip...";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var result = await _setupWizardService.InstallDiarizationRuntimeAsync();
            SetupDiarizationRuntimeSummary = result.IsSucceeded
                ? $"話者識別ランタイムを導入しました: {result.InstallPath}"
                : $"話者識別ランタイムの導入に失敗しました: {result.Message}";
            CompleteModelDownloadProgress(displayName, result.IsSucceeded, result.IsSucceeded ? null : result.Message);
            LatestLog = result.Message;
        }
        catch (OperationCanceledException)
        {
            CompleteModelDownloadProgress(displayName, succeeded: false, "diarize runtime install was cancelled.");
            LatestLog = "diarize runtime install was cancelled.";
        }

        RefreshSetupWizard();
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
        var stateBeforeRecommendation = _setupWizardService.LoadState();
        _setupState = _setupWizardService.ApplyAutomaticModelPresetRecommendation();
        if (!string.Equals(
                stateBeforeRecommendation.SelectedModelPresetId,
                _setupState.SelectedModelPresetId,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(_setupState.SelectedAsrModelId))
        {
            SelectedAsrEngineId = _setupState.SelectedAsrModelId;
        }

        _setupPresetRecommendation = _setupWizardService.GetPresetRecommendation();
        _setupState = _setupWizardService.LoadState();
        RefreshDiarizationRuntimeSummary();
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

        SetupModelPresetChoices.Clear();
        foreach (var preset in _setupWizardService.GetModelPresets())
        {
            SetupModelPresetChoices.Add(preset);
        }

        _selectedSetupAsrModel = SetupAsrModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(_setupState.SelectedAsrModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupAsrModelChoices.FirstOrDefault();
        _selectedSetupReviewModel = SetupReviewModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(_setupState.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupReviewModelChoices.FirstOrDefault();
        _selectedSetupModelPreset = SetupModelPresetChoices.FirstOrDefault(preset =>
            preset.PresetId.Equals(_setupState.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase));

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
        OnPropertyChanged(nameof(SelectedSetupModelPreset));
        OnPropertyChanged(nameof(AsrModel));
        OnPropertyChanged(nameof(ReviewModel));
        OnPropertyChanged(nameof(SelectedSetupModelPresetDescription));
        OnPropertyChanged(nameof(SelectedSetupModelPresetModels));
        OnPropertyChanged(nameof(SelectedSetupModelsReady));
        OnPropertyChanged(nameof(SetupDiarizationRuntimeReady));
        OnPropertyChanged(nameof(SelectedSetupConfigurationReady));
        OnPropertyChanged(nameof(SetupPrimaryInstallActionText));
        OnPropertyChanged(nameof(SetupPrimaryInstallSummary));
        OnPropertyChanged(nameof(SetupPresetRecommendationSummary));
        OnPropertyChanged(nameof(SetupPresetRecommendationDetail));
        OnPropertyChanged(nameof(SetupCurrentStep));
        OnPropertyChanged(nameof(SetupStepDisplayName));
        OnPropertyChanged(nameof(SetupStatusSummary));
        OnPropertyChanged(nameof(SetupWizardModalTitle));
        OnPropertyChanged(nameof(SetupWizardModalGuide));
        OnPropertyChanged(nameof(SetupMode));
        OnPropertyChanged(nameof(SetupStorageRoot));
        OnPropertyChanged(nameof(SetupLicenseAccepted));
        OnPropertyChanged(nameof(SetupDiarizationRuntimeSummary));
        OnPropertyChanged(nameof(IsSetupComplete));
        OnPropertyChanged(nameof(RequiredRuntimeAssetsReady));
        OnPropertyChanged(nameof(ReviewStageAssetsReady));
        OnPropertyChanged(nameof(CanRunSelectedJob));
        OnPropertyChanged(nameof(RunPreflightSummary));
        OnPropertyChanged(nameof(RunPreflightDetail));
        SetupModelAudits.Clear();
        foreach (var audit in _setupWizardService.GetSelectedModelAudit())
        {
            SetupModelAudits.Add(audit);
        }

        SetupExistingData.Clear();
        foreach (var item in _setupWizardService.GetExistingDataSummary())
        {
            SetupExistingData.Add(item);
        }

        if (RunSelectedJobCommand is RelayCommand runCommand)
        {
            runCommand.RaiseCanExecuteChanged();
        }

        UpdateSetupDownloadCommandStates();
    }

    private void UpdateSetupDownloadCommandStates()
    {
        if (SetupInstallSelectedPresetCommand is RelayCommand installPresetCommand)
        {
            installPresetCommand.RaiseCanExecuteChanged();
        }

        if (SetupDownloadAsrCommand is RelayCommand asrCommand)
        {
            asrCommand.RaiseCanExecuteChanged();
        }

        if (SetupDownloadReviewCommand is RelayCommand reviewCommand)
        {
            reviewCommand.RaiseCanExecuteChanged();
        }

        if (SetupInstallDiarizationRuntimeCommand is RelayCommand diarizationCommand)
        {
            diarizationCommand.RaiseCanExecuteChanged();
        }
    }

    private static string FindSetupModelDisplayName(IEnumerable<ModelCatalogEntry> models, string modelId)
    {
        return models.FirstOrDefault(model =>
            model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))?.DisplayName ?? modelId;
    }

    private void RefreshDiarizationRuntimeSummary()
    {
        var isInstalled = DiarizationRuntimeLayout.HasPackage(Paths);
        SetupDiarizationRuntimeSummary = isInstalled
            ? $"話者識別ランタイム導入済み: {Paths.PythonPackages}"
            : "話者識別ランタイムは未導入です。必要な場合だけ追加導入できます。";
    }
}
