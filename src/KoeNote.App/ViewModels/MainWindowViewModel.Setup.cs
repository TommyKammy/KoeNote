using System.IO;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
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
        RefreshLlmSettingsDisplay(synchronizeFromSetup: true);
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
            RefreshLlmSettingsDisplay(synchronizeFromSetup: true);
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

            if (!SetupFasterWhisperRuntimeReady)
            {
                ModelDownloadProgressSummary = $"Installing {displayName}: checking bundled Python and pip for ASR runtime...";
                IsModelDownloadProgressIndeterminate = true;
                var preflight = await _setupWizardService.CheckFasterWhisperRuntimeInstallPreflightAsync();
                if (!preflight.IsReady)
                {
                    var message = BuildFasterWhisperRuntimeSetupFailureMessage(preflight.Message, preflight.FailureCategory);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

                ModelDownloadProgressSummary = $"Installing {displayName}: installing ASR runtime with bundled Python...";
                var runtimeResult = await _setupWizardService.InstallFasterWhisperRuntimeAsync();
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildFasterWhisperRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

                LatestLog = runtimeResult.Message;
            }

            if (!SetupDiarizationRuntimeReady)
            {
                ModelDownloadProgressSummary = $"Installing {displayName}: checking bundled Python and pip for speaker diarization...";
                IsModelDownloadProgressIndeterminate = true;
                var preflight = await _setupWizardService.CheckDiarizationRuntimeInstallPreflightAsync();
                if (!preflight.IsReady)
                {
                    var message = BuildDiarizationRuntimeSetupFailureMessage(preflight.Message, preflight.FailureCategory);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    SetupDiarizationRuntimeSummary = message;
                    LatestLog = message;
                    return;
                }

                ModelDownloadProgressSummary = $"Installing {displayName}: installing speaker diarization runtime with bundled Python...";
                var runtimeResult = await _setupWizardService.InstallDiarizationRuntimeAsync();
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildDiarizationRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    SetupDiarizationRuntimeSummary = message;
                    LatestLog = message;
                    return;
                }

                SetupDiarizationRuntimeSummary = $"Speaker diarization runtime installed: {runtimeResult.InstallPath}";
                LatestLog = runtimeResult.Message;
            }

            if (!SetupTernaryReviewRuntimeReady)
            {
                ModelDownloadProgressSummary = $"Installing {displayName}: downloading Ternary review runtime...";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallTernaryReviewRuntimeAsync();
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildTernaryReviewRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

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
        ModelDownloadProgressSummary = "Checking bundled Python and pip for diarize runtime...";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var preflight = await _setupWizardService.CheckDiarizationRuntimeInstallPreflightAsync();
            if (!preflight.IsReady)
            {
                var preflightMessage = BuildDiarizationRuntimeSetupFailureMessage(preflight.Message, preflight.FailureCategory);
                RefreshSetupWizard();
                SetupDiarizationRuntimeSummary = preflightMessage;
                CompleteModelDownloadProgress(displayName, succeeded: false, preflightMessage);
                LatestLog = preflightMessage;
                return;
            }

            ModelDownloadProgressSummary = "Installing diarize runtime with bundled Python...";
            var result = await _setupWizardService.InstallDiarizationRuntimeAsync();
            RefreshSetupWizard();
            var message = result.IsSucceeded
                ? $"Speaker diarization runtime installed: {result.InstallPath}"
                : BuildDiarizationRuntimeSetupFailureMessage(result.Message, result.FailureCategory);
            SetupDiarizationRuntimeSummary = message;
            CompleteModelDownloadProgress(displayName, result.IsSucceeded, result.IsSucceeded ? null : message);
            LatestLog = result.IsSucceeded ? result.Message : message;
        }
        catch (OperationCanceledException)
        {
            RefreshSetupWizard();
            CompleteModelDownloadProgress(displayName, succeeded: false, "diarize runtime install was cancelled.");
            LatestLog = "diarize runtime install was cancelled.";
        }
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
            if (role.Equals("review", StringComparison.OrdinalIgnoreCase))
            {
                RefreshLlmSettingsDisplay(synchronizeFromSetup: true);
            }

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
        OnPropertyChanged(nameof(SetupFasterWhisperRuntimeReady));
        OnPropertyChanged(nameof(SetupDiarizationRuntimeReady));
        OnPropertyChanged(nameof(SetupTernaryReviewRuntimeReady));
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
        var installPath = Directory.Exists(Paths.DiarizationPythonEnvironment)
            ? Paths.DiarizationPythonEnvironment
            : Paths.PythonPackages;
        SetupDiarizationRuntimeSummary = isInstalled
            ? $"Speaker diarization runtime installed: {installPath}"
            : $"Speaker diarization runtime is not installed. KoeNote will use bundled Python 3.12 if available: {Paths.BundledPythonPath}";
    }

    private static string BuildDiarizationRuntimeSetupFailureMessage(string message, string failureCategory)
    {
        return failureCategory switch
        {
            DiarizationRuntimeService.FailureCategoryPythonSourceUnavailable =>
                $"Speaker diarization needs bundled Python 3.12. Put python.exe under tools\\python, then retry. Details: {message}",
            DiarizationRuntimeService.FailureCategoryVenvCreationFailed =>
                $"KoeNote found Python, but could not create the managed diarization environment. Details: {message}",
            DiarizationRuntimeService.FailureCategoryTorchWheelUnavailable =>
                $"diarize could not be installed because compatible torch wheels were not available. Use bundled Python 3.12 and retry. Details: {message}",
            DiarizationRuntimeService.FailureCategoryNetworkUnavailable =>
                $"diarize could not be downloaded. Check the network connection or proxy settings, then retry. Details: {message}",
            DiarizationRuntimeService.FailureCategoryPipInstallFailed =>
                $"KoeNote found Python, but pip could not install diarize. Details: {message}",
            DiarizationRuntimeService.FailureCategoryPackageCheckFailed =>
                $"diarize was installed, but KoeNote could not verify the required version. Details: {message}",
            _ => message
        };
    }

    private static string BuildFasterWhisperRuntimeSetupFailureMessage(string message, string failureCategory)
    {
        return failureCategory switch
        {
            FasterWhisperRuntimeService.FailureCategoryPythonSourceUnavailable =>
                $"ASR needs bundled Python 3.12. Put python.exe under tools\\python, then retry. Details: {message}",
            FasterWhisperRuntimeService.FailureCategoryVenvCreationFailed =>
                $"KoeNote found Python, but could not create the managed ASR environment. Details: {message}",
            FasterWhisperRuntimeService.FailureCategoryNetworkUnavailable =>
                $"faster-whisper could not be downloaded. Check the network connection or proxy settings, then retry. Details: {message}",
            FasterWhisperRuntimeService.FailureCategoryPipInstallFailed =>
                $"KoeNote found Python, but pip could not install faster-whisper. Details: {message}",
            FasterWhisperRuntimeService.FailureCategoryPackageCheckFailed =>
                $"faster-whisper was installed, but KoeNote could not verify the package. Details: {message}",
            _ => message
        };
    }

    private static string BuildTernaryReviewRuntimeSetupFailureMessage(string message, string failureCategory)
    {
        return failureCategory switch
        {
            TernaryReviewRuntimeService.FailureCategoryNetworkUnavailable =>
                $"Ternary review runtime could not be downloaded. Check the network connection or proxy settings, then retry. Details: {message}",
            TernaryReviewRuntimeService.FailureCategoryArchiveInvalid =>
                $"Ternary review runtime was downloaded, but the archive was not usable. Details: {message}",
            TernaryReviewRuntimeService.FailureCategoryInstallFailed =>
                $"Ternary review runtime could not be installed under tools\\review-ternary. Details: {message}",
            _ => message
        };
    }
}
