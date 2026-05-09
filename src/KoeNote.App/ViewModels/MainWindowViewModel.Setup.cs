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
        if (_setupState.CurrentStep is SetupStep.InstallPlan or SetupStep.Install)
        {
            if (SelectedSetupConfigurationReady)
            {
                _setupState = _setupWizardService.MoveNext();
                RefreshSetupWizard();
                LatestLog = $"Setup moved to {_setupState.CurrentStep}.";
                return Task.CompletedTask;
            }

            return SetupInstallSelectedPresetAsync();
        }

        if (_setupState.CurrentStep is SetupStep.SmokeTest or SetupStep.Complete && !SelectedSetupConfigurationReady)
        {
            _setupState = _setupWizardService.MoveToInstallPlan();
            RefreshSetupWizard();
            return SetupInstallSelectedPresetAsync();
        }

        if (_setupState.CurrentStep == SetupStep.Complete && _setupState.IsCompleted)
        {
            IsSetupWizardModalOpen = false;
            LatestLog = "Setup is complete. KoeNote is ready.";
            return Task.CompletedTask;
        }

        if (_setupState.CurrentStep is SetupStep.SmokeTest or SetupStep.Complete)
        {
            return SetupCompleteAsync();
        }

        _setupState = _setupWizardService.MoveNextGuided();
        if (!string.IsNullOrWhiteSpace(_setupState.SelectedAsrModelId))
        {
            SelectedAsrEngineId = _setupState.SelectedAsrModelId;
        }

        RefreshSetupWizard();
        LatestLog = $"Setup moved to {_setupState.CurrentStep}.";
        return Task.CompletedTask;
    }

    private bool CanUseSetupNextAction()
    {
        if (IsModelDownloadInProgress)
        {
            return false;
        }

        if (_setupState.IsCompleted)
        {
            return _setupState.CurrentStep == SetupStep.Complete;
        }

        if (_setupState.CurrentStep is SetupStep.InstallPlan or SetupStep.Install)
        {
            return SelectedSetupConfigurationReady || CanInstallSelectedPreset();
        }

        return true;
    }

    private bool CanUseSetupBackAction()
    {
        return !IsModelDownloadInProgress &&
            _setupState.CurrentStep is not SetupStep.Install and not SetupStep.Complete;
    }

    private bool CanCloseSetupWizardModal()
    {
        return !IsModelDownloadInProgress;
    }

    private Task SetupUseRecommendedAsync()
    {
        _setupState = _setupWizardService.UseRecommendedSelections();
        SelectedAsrEngineId = _setupState.SelectedAsrModelId ?? SelectedAsrEngineId;
        RefreshSetupWizard();
        RefreshLlmSettingsDisplay(synchronizeFromSetup: true);
        LatestLog = "Recommended setup choices selected. Accept licenses before installation.";
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
        LatestLog = "Setup licenses accepted. Review the installation plan, then start installation.";
        return Task.CompletedTask;
    }

    private bool CanInstallSelectedPreset()
    {
        return !IsModelDownloadInProgress &&
            SelectedSetupAsrModel is not null &&
            SelectedSetupReviewModel is not null &&
            (!_setupState.LicenseAccepted ||
                !SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan) ||
                !SelectedSetupConfigurationReady);
    }

    private bool CanCancelSetupInstall()
    {
        return IsModelDownloadInProgress;
    }

    private Task SetupCancelInstallAsync()
    {
        _modelDownloadCancellation?.Cancel();
        LatestLog = "Setup installation cancellation requested.";
        return Task.CompletedTask;
    }

    private bool CanDownloadSetupAsr()
    {
        return !IsModelDownloadInProgress &&
            _setupState.LicenseAccepted &&
            SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan) &&
            SelectedSetupAsrModel is not null &&
            !IsSetupModelReady(SelectedSetupAsrModel);
    }

    private bool CanDownloadSetupReview()
    {
        return !IsModelDownloadInProgress &&
            _setupState.LicenseAccepted &&
            SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan) &&
            SelectedSetupReviewModel is not null &&
            !IsSetupModelReady(SelectedSetupReviewModel);
    }

    private bool CanInstallCudaReviewRuntime()
    {
        return !IsModelDownloadInProgress &&
            _setupState.LicenseAccepted &&
            SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan) &&
            SetupCudaReviewRuntimeRecommended &&
            !SetupCudaReviewRuntimeReady;
    }

    private bool CanInstallAsrCudaRuntime()
    {
        return !IsModelDownloadInProgress &&
            _setupState.LicenseAccepted &&
            SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan) &&
            SetupAsrCudaRuntimeRecommended &&
            !SetupAsrCudaRuntimeReady;
    }

    private bool CanInstallDiarizationRuntime()
    {
        return !IsModelDownloadInProgress &&
            _setupState.LicenseAccepted &&
            SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan) &&
            !SetupDiarizationRuntimeReady;
    }

    private bool CanUseSetupInstallActions()
    {
        return !IsModelDownloadInProgress &&
            _setupState.LicenseAccepted &&
            SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan);
    }

    private async Task SetupInstallSelectedPresetAsync()
    {
        if (IsModelDownloadInProgress)
        {
            return;
        }

        if (!_setupState.LicenseAccepted)
        {
            _setupState = _setupWizardService.AcceptLicenses();
            RefreshSetupWizard();
            LatestLog = "Setup licenses accepted. Review the installation plan, then start installation.";
        }

        if (!SetupStepFlow.HasReached(_setupState.CurrentStep, SetupStep.InstallPlan))
        {
            _setupState = _setupWizardService.MoveToInstallPlan();
            RefreshSetupWizard();
            LatestLog = "Review the installation plan before installation.";
            return;
        }

        var displayName = SelectedSetupModelPreset?.DisplayName ?? "selected model preset";
        _modelDownloadCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _modelDownloadCancellation = cancellation;
        ResetSetupInstallStatuses();
        BeginModelDownloadProgress(displayName);
        ModelDownloadProgressSummary = $"Installing {displayName}: checking ASR, Review, and speaker diarization runtime...";
        SetSetupInstallStatus("文字起こしモデル", "導入中", SelectedSetupAsrModel?.DisplayName ?? "ASR model");
        SetSetupInstallStatus("整文モデル", "導入中", SelectedSetupReviewModel?.DisplayName ?? "Review model");
        var optionalFailureMessages = new List<string>();
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
            var modelResult = await _setupWizardService.InstallSelectedPresetModelsAsync(progress, cancellation.Token);
            RefreshSetupWizard();
            if (!modelResult.IsSucceeded)
            {
                SetSetupInstallStatus("文字起こしモデル", "失敗", modelResult.Message);
                SetSetupInstallStatus("整文モデル", "失敗", modelResult.Message);
                CompleteSetupModelDownload(displayName, modelResult);
                return;
            }

            SetSetupInstallStatus("文字起こしモデル", "完了", SelectedSetupAsrModel?.DisplayName ?? "ASR model");
            SetSetupInstallStatus("整文モデル", "完了", SelectedSetupReviewModel?.DisplayName ?? "Review model");

            if (!SetupFasterWhisperRuntimeReady)
            {
                SetSetupInstallStatus("ASR runtime", "導入中", "文字起こしに必要な実行環境");
                ModelDownloadProgressSummary = $"Installing {displayName}: checking bundled Python and pip for ASR runtime...";
                IsModelDownloadProgressIndeterminate = true;
                var preflight = await _setupWizardService.CheckFasterWhisperRuntimeInstallPreflightAsync(cancellation.Token);
                if (!preflight.IsReady)
                {
                    var message = BuildFasterWhisperRuntimeSetupFailureMessage(preflight.Message, preflight.FailureCategory);
                    SetSetupInstallStatus("ASR runtime", "失敗", message);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

                ModelDownloadProgressSummary = $"Installing {displayName}: installing ASR runtime with bundled Python...";
                var runtimeResult = await _setupWizardService.InstallFasterWhisperRuntimeAsync(cancellation.Token);
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildFasterWhisperRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    SetSetupInstallStatus("ASR runtime", "失敗", message);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

                SetSetupInstallStatus("ASR runtime", "完了", runtimeResult.InstallPath);
                LatestLog = runtimeResult.Message;
            }
            else
            {
                SetSetupInstallStatus("ASR runtime", "スキップ", "導入済みです");
            }

            if (SetupAsrCudaRuntimeRecommended && !SetupAsrCudaRuntimeReady)
            {
                SetSetupInstallStatus("ASR GPU runtime", "導入中", "NVIDIA GPU向けのASR CUDA runtime");
                ModelDownloadProgressSummary = $"Installing {displayName}: downloading CUDA ASR runtime for selected ASR model...";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallAsrCudaRuntimeAsync(cancellation.Token);
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildAsrCudaRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    SetSetupInstallStatus("ASR GPU runtime", "失敗", message);
                    SetupAsrCudaRuntimeSummary = message;
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

                SetupAsrCudaRuntimeSummary = $"CUDA ASR runtime installed: {runtimeResult.InstallPath}";
                SetSetupInstallStatus("ASR GPU runtime", "完了", runtimeResult.InstallPath);
                LatestLog = runtimeResult.Message;
            }
            else if (SetupAsrCudaRuntimeRecommended)
            {
                SetSetupInstallStatus("ASR GPU runtime", "スキップ", "導入済みです");
            }

            if (!SetupDiarizationRuntimeReady)
            {
                SetSetupInstallStatus("話者識別", "導入中", "話者分離を使うための追加runtime");
                ModelDownloadProgressSummary = $"Installing {displayName}: checking bundled Python and pip for speaker diarization...";
                IsModelDownloadProgressIndeterminate = true;
                var preflight = await _setupWizardService.CheckDiarizationRuntimeInstallPreflightAsync(cancellation.Token);
                if (!preflight.IsReady)
                {
                    var message = BuildOptionalDiarizationRuntimeFailureMessage(preflight.Message, preflight.FailureCategory);
                    SetSetupInstallStatus("話者識別", "失敗", message);
                    SetupDiarizationRuntimeSummary = message;
                    optionalFailureMessages.Add(message);
                    LatestLog = message;
                }
                else
                {
                    ModelDownloadProgressSummary = $"Installing {displayName}: installing speaker diarization runtime with bundled Python...";
                    var runtimeResult = await _setupWizardService.InstallDiarizationRuntimeAsync(cancellation.Token);
                    RefreshSetupWizard();
                    if (!runtimeResult.IsSucceeded)
                    {
                        var message = BuildOptionalDiarizationRuntimeFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                        SetSetupInstallStatus("話者識別", "失敗", message);
                        SetupDiarizationRuntimeSummary = message;
                        optionalFailureMessages.Add(message);
                        LatestLog = message;
                    }
                    else
                    {
                        SetupDiarizationRuntimeSummary = $"Speaker diarization runtime installed: {runtimeResult.InstallPath}";
                        SetSetupInstallStatus("話者識別", "完了", runtimeResult.InstallPath);
                        LatestLog = runtimeResult.Message;
                    }
                }
            }
            else
            {
                SetSetupInstallStatus("話者識別", "スキップ", "導入済みです");
            }

            if (SetupCudaReviewRuntimeRecommended && !SetupCudaReviewRuntimeReady)
            {
                SetSetupInstallStatus("GPU高速化", "導入中", "検出されたNVIDIA GPU向けのReview runtime");
                ModelDownloadProgressSummary = $"Installing {displayName}: downloading CUDA review runtime for detected NVIDIA GPU...";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallCudaReviewRuntimeAsync(cancellation.Token);
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildOptionalCudaReviewRuntimeFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    SetSetupInstallStatus("GPU高速化", "失敗", message);
                    SetupCudaReviewRuntimeSummary = message;
                    optionalFailureMessages.Add(message);
                    LatestLog = message;
                }
                else
                {
                    SetupCudaReviewRuntimeSummary = $"CUDA review runtime installed: {runtimeResult.InstallPath}";
                    SetSetupInstallStatus("GPU高速化", "完了", runtimeResult.InstallPath);
                    LatestLog = runtimeResult.Message;
                }
            }
            else if (SetupCudaReviewRuntimeRecommended)
            {
                SetSetupInstallStatus("GPU高速化", "スキップ", "導入済みです");
            }

            if (!SetupTernaryReviewRuntimeReady)
            {
                SetSetupInstallStatus("Ternary review runtime", "導入中", "選択した整文モデルに必要なruntime");
                ModelDownloadProgressSummary = $"Installing {displayName}: downloading Ternary review runtime...";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallTernaryReviewRuntimeAsync(cancellation.Token);
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildTernaryReviewRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    SetSetupInstallStatus("Ternary review runtime", "失敗", message);
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }

                SetSetupInstallStatus("Ternary review runtime", "完了", runtimeResult.InstallPath);
                LatestLog = runtimeResult.Message;
            }

            SetSetupInstallStatus("保存先", "確認済み", SetupStorageRoot);
            CompleteModelDownloadProgress(displayName, succeeded: true);
            if (optionalFailureMessages.Count > 0)
            {
                ModelDownloadProgressSummary = "基本構成の導入が完了しました。一部の追加機能は後から追加導入できます。";
                ModelDownloadNotification = ModelDownloadProgressSummary;
            }
            else
            {
                ModelDownloadProgressSummary = "導入が完了しました。最終確認へ進めます。";
                ModelDownloadNotification = "導入が完了しました。KoeNoteを使う準備に進めます。";
            }

            _setupState = _setupWizardService.MoveNext();
            RefreshSetupWizard();
            LatestLog = ModelDownloadProgressSummary;
        }
        catch (OperationCanceledException)
        {
            _setupState = _setupWizardService.MoveToPresetSelection();
            RefreshSetupWizard();
            CompleteModelDownloadProgress(displayName, succeeded: false, "セットアップ導入をキャンセルしました。別のプリセットを選べます。");
            LatestLog = "Setup installation was cancelled. Select a different preset or retry.";
        }
        finally
        {
            if (ReferenceEquals(_modelDownloadCancellation, cancellation))
            {
                _modelDownloadCancellation = null;
            }

            UpdateSetupDownloadCommandStates();
        }
    }

    private async Task SetupDownloadAsrAsync()
    {
        var displayName = SelectedSetupAsrModel?.DisplayName ?? "ASR model";
        ResetSetupInstallStatuses();
        BeginModelDownloadProgress(displayName);
        SetSetupInstallStatus("文字起こしモデル", "導入中", displayName);
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            UpdateModelDownloadProgress(displayName, downloadProgress);
            RefreshModelCatalogForDownloadProgress(downloadProgress);
        });
        var result = await _setupWizardService.DownloadSelectedModelAsync("asr", progress);
        RefreshSetupWizard();
        SetSetupInstallStatus("文字起こしモデル", result.IsSucceeded ? "完了" : "失敗", result.Message);
        CompleteSetupModelDownload(displayName, result);
    }

    private async Task SetupDownloadReviewAsync()
    {
        var displayName = SelectedSetupReviewModel?.DisplayName ?? "Review LLM model";
        ResetSetupInstallStatuses();
        BeginModelDownloadProgress(displayName);
        SetSetupInstallStatus("整文モデル", "導入中", displayName);
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            UpdateModelDownloadProgress(displayName, downloadProgress);
            RefreshModelCatalogForDownloadProgress(downloadProgress);
        });
        var result = await _setupWizardService.DownloadSelectedModelAsync("review", progress);
        RefreshSetupWizard();
        SetSetupInstallStatus("整文モデル", result.IsSucceeded ? "完了" : "失敗", result.Message);
        CompleteSetupModelDownload(displayName, result);
    }

    private async Task SetupInstallDiarizationRuntimeAsync()
    {
        const string displayName = "diarize speaker diarization runtime";
        ResetSetupInstallStatuses();
        BeginModelDownloadProgress(displayName);
        SetSetupInstallStatus("話者識別", "導入中", "話者分離を使うための追加runtime");
        ModelDownloadProgressSummary = "Checking bundled Python and pip for diarize runtime...";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var preflight = await _setupWizardService.CheckDiarizationRuntimeInstallPreflightAsync();
            if (!preflight.IsReady)
            {
                var preflightMessage = BuildOptionalDiarizationRuntimeFailureMessage(preflight.Message, preflight.FailureCategory);
                RefreshSetupWizard();
                SetSetupInstallStatus("話者識別", "失敗", preflightMessage);
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
                : BuildOptionalDiarizationRuntimeFailureMessage(result.Message, result.FailureCategory);
            SetupDiarizationRuntimeSummary = message;
            SetSetupInstallStatus("話者識別", result.IsSucceeded ? "完了" : "失敗", message);
            CompleteModelDownloadProgress(displayName, result.IsSucceeded, result.IsSucceeded ? null : message);
            if (result.IsSucceeded)
            {
                ModelDownloadNotification = "話者識別runtimeの導入が完了しました。";
            }
            LatestLog = result.IsSucceeded ? result.Message : message;
        }
        catch (OperationCanceledException)
        {
            RefreshSetupWizard();
            const string message = "話者識別runtimeの導入を中止しました。KoeNote本体は利用できます。必要になったら、モデルと保存先の手動設定から後で追加導入できます。";
            SetSetupInstallStatus("話者識別", "失敗", message);
            CompleteModelDownloadProgress(displayName, succeeded: false, message);
            LatestLog = message;
        }
    }

    private async Task SetupInstallCudaReviewRuntimeAsync()
    {
        const string displayName = "CUDA review runtime";
        ResetSetupInstallStatuses();
        BeginModelDownloadProgress(displayName);
        SetSetupInstallStatus("GPU高速化", "導入中", "検出されたNVIDIA GPU向けのReview runtime");
        ModelDownloadProgressSummary = "Installing CUDA review runtime for LLM acceleration...";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var result = await _setupWizardService.InstallCudaReviewRuntimeAsync();
            RefreshSetupWizard();
            var message = result.IsSucceeded
                ? $"CUDA review runtime installed: {result.InstallPath}"
                : BuildOptionalCudaReviewRuntimeFailureMessage(result.Message, result.FailureCategory);
            SetupCudaReviewRuntimeSummary = message;
            SetSetupInstallStatus("GPU高速化", result.IsSucceeded ? "完了" : "失敗", message);
            CompleteModelDownloadProgress(displayName, result.IsSucceeded, result.IsSucceeded ? null : message);
            if (result.IsSucceeded)
            {
                ModelDownloadNotification = "GPU高速化runtimeの導入が完了しました。";
            }
            LatestLog = result.IsSucceeded ? result.Message : message;
        }
        catch (OperationCanceledException)
        {
            RefreshSetupWizard();
            const string message = "GPU高速化runtimeの導入を中止しました。KoeNote本体は利用できます。整文はCPU版で続行できます。必要になったら、モデルと保存先の手動設定から後で追加導入できます。";
            SetSetupInstallStatus("GPU高速化", "失敗", message);
            CompleteModelDownloadProgress(displayName, succeeded: false, message);
            LatestLog = message;
        }
    }

    private async Task SetupInstallAsrCudaRuntimeAsync()
    {
        const string displayName = "CUDA ASR runtime";
        ResetSetupInstallStatuses();
        BeginModelDownloadProgress(displayName);
        SetSetupInstallStatus("ASR GPU runtime", "導入中", "NVIDIA GPU向けのASR CUDA runtime");
        ModelDownloadProgressSummary = "Installing CUDA ASR runtime for faster-whisper GPU execution...";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var result = await _setupWizardService.InstallAsrCudaRuntimeAsync();
            RefreshSetupWizard();
            var message = result.IsSucceeded
                ? $"CUDA ASR runtime installed: {result.InstallPath}"
                : BuildAsrCudaRuntimeSetupFailureMessage(result.Message, result.FailureCategory);
            SetupAsrCudaRuntimeSummary = message;
            SetSetupInstallStatus("ASR GPU runtime", result.IsSucceeded ? "完了" : "失敗", message);
            CompleteModelDownloadProgress(displayName, result.IsSucceeded, result.IsSucceeded ? null : message);
            if (result.IsSucceeded)
            {
                ModelDownloadNotification = "ASR GPU runtimeの導入が完了しました。";
            }

            LatestLog = result.IsSucceeded ? result.Message : message;
        }
        catch (OperationCanceledException)
        {
            RefreshSetupWizard();
            const string message = "ASR GPU runtimeの導入を中止しました。";
            SetSetupInstallStatus("ASR GPU runtime", "失敗", message);
            CompleteModelDownloadProgress(displayName, succeeded: false, message);
            LatestLog = message;
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
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeRecommended));
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeReady));
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeActionText));
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeRecommended));
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeReady));
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeActionText));
        OnPropertyChanged(nameof(SetupTernaryReviewRuntimeReady));
        OnPropertyChanged(nameof(SelectedSetupConfigurationReady));
        OnPropertyChanged(nameof(SetupPrimaryInstallActionText));
        OnPropertyChanged(nameof(SetupPrimaryInstallSummary));
        OnPropertyChanged(nameof(SetupNextActionText));
        OnPropertyChanged(nameof(ShowSetupInlineInstallAction));
        OnPropertyChanged(nameof(ShowSetupLicenseNotice));
        OnPropertyChanged(nameof(ShowSetupSmokeAction));
        OnPropertyChanged(nameof(ShowSetupCompleteAction));
        OnPropertyChanged(nameof(SetupLicenseNoticeText));
        OnPropertyChanged(nameof(SetupPresetRecommendationSummary));
        OnPropertyChanged(nameof(SetupPresetRecommendationDetail));
        OnPropertyChanged(nameof(SetupCurrentStep));
        OnPropertyChanged(nameof(SetupStepDisplayName));
        OnPropertyChanged(nameof(SetupStatusSummary));
        OnPropertyChanged(nameof(SetupCompleteActionText));
        OnPropertyChanged(nameof(SetupWizardModalTitle));
        OnPropertyChanged(nameof(SetupWizardModalGuide));
        OnPropertyChanged(nameof(SetupMode));
        OnPropertyChanged(nameof(SetupStorageRoot));
        OnPropertyChanged(nameof(SetupLicenseAccepted));
        OnPropertyChanged(nameof(SetupDiarizationRuntimeSummary));
        RefreshAsrCudaRuntimeSummary();
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeSummary));
        RefreshCudaReviewRuntimeSummary();
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeSummary));
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

        RefreshSetupInstallPlanItems();

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

        if (SetupCancelInstallCommand is RelayCommand cancelInstallCommand)
        {
            cancelInstallCommand.RaiseCanExecuteChanged();
        }

        if (SetupNextCommand is RelayCommand nextCommand)
        {
            nextCommand.RaiseCanExecuteChanged();
        }

        if (SetupBackCommand is RelayCommand backCommand)
        {
            backCommand.RaiseCanExecuteChanged();
        }

        if (CloseSetupWizardModalCommand is RelayCommand closeSetupCommand)
        {
            closeSetupCommand.RaiseCanExecuteChanged();
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

        if (SetupInstallAsrCudaRuntimeCommand is RelayCommand asrCudaCommand)
        {
            asrCudaCommand.RaiseCanExecuteChanged();
        }

        if (SetupInstallCudaReviewRuntimeCommand is RelayCommand cudaReviewCommand)
        {
            cudaReviewCommand.RaiseCanExecuteChanged();
        }

        if (SetupRegisterLocalAsrCommand is RelayCommand registerAsrCommand)
        {
            registerAsrCommand.RaiseCanExecuteChanged();
        }

        if (SetupRegisterLocalReviewCommand is RelayCommand registerReviewCommand)
        {
            registerReviewCommand.RaiseCanExecuteChanged();
        }

        if (SetupImportOfflinePackCommand is RelayCommand importPackCommand)
        {
            importPackCommand.RaiseCanExecuteChanged();
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

    private void RefreshCudaReviewRuntimeSummary()
    {
        if (SetupCudaReviewRuntimeReady)
        {
            SetupCudaReviewRuntimeSummary = $"CUDA review runtime installed: {Paths.ReviewRuntimeDirectory}";
            return;
        }

        SetupCudaReviewRuntimeSummary = SetupCudaReviewRuntimeRecommended
            ? $"NVIDIA GPU detected. CUDA review runtime will be included in bundled setup when configured. CPU fallback remains available: {Paths.ReviewRuntimeDirectory}"
            : "CUDA review runtime is optional and disabled because no NVIDIA GPU was detected. CPU review runtime will be used.";
    }

    private void RefreshAsrCudaRuntimeSummary()
    {
        if (SetupAsrCudaRuntimeReady)
        {
            SetupAsrCudaRuntimeSummary = $"CUDA ASR runtime installed: {Paths.AsrRuntimeDirectory}";
            return;
        }

        SetupAsrCudaRuntimeSummary = SetupAsrCudaRuntimeRecommended
            ? $"NVIDIA GPU detected. The selected ASR model should use CUDA; install CUDA ASR runtime: {Paths.AsrRuntimeDirectory}"
            : "CUDA ASR runtime is optional for the selected ASR model.";
    }

    private IReadOnlyList<SetupInstallPlanItem> BuildSetupInstallPlanItems()
    {
        var items = new List<SetupInstallPlanItem>
        {
            new(
                "文字起こしモデル",
                SelectedSetupAsrModel?.DisplayName ?? "未選択",
                IsSetupModelReady(SelectedSetupAsrModel) ? "導入済み" : "導入します"),
            new(
                "整文モデル",
                SelectedSetupReviewModel?.DisplayName ?? "未選択",
                IsSetupModelReady(SelectedSetupReviewModel) ? "導入済み" : "導入します"),
            new(
                "ASR runtime",
                "文字起こしに必要な実行環境",
                SetupFasterWhisperRuntimeReady ? "導入済み" : "導入します"),
            new(
                "話者識別",
                "話者分離を使うための追加runtime",
                SetupDiarizationRuntimeReady ? "導入済み" : "導入します")
        };

        if (SetupAsrCudaRuntimeRecommended)
        {
            items.Add(new(
                "ASR GPU runtime",
                "NVIDIA GPU向けのASR CUDA runtime",
                SetupAsrCudaRuntimeReady ? "導入済み" : "導入します"));
        }

        if (SetupCudaReviewRuntimeRecommended)
        {
            items.Add(new(
                "GPU高速化",
                "検出されたNVIDIA GPU向けのReview runtime",
                SetupCudaReviewRuntimeReady ? "導入済み" : "導入します"));
        }

        if (!SetupTernaryReviewRuntimeReady)
        {
            items.Add(new(
                "Ternary review runtime",
                "選択した整文モデルに必要なruntime",
                "導入します"));
        }

        items.Add(new(
            "保存先",
            "KoeNoteの標準フォルダー",
            Directory.Exists(SetupStorageRoot) ? "確認済み" : "作成します"));
        return items
            .Select(item => _setupInstallStatusOverrides.TryGetValue(item.Name, out var status)
                ? status
                : item)
            .ToArray();
    }

    private void ResetSetupInstallStatuses()
    {
        _setupInstallStatusOverrides.Clear();
        RefreshSetupInstallPlanItems();
    }

    private void SetSetupInstallStatus(string name, string status, string summary)
    {
        _setupInstallStatusOverrides[name] = new SetupInstallPlanItem(name, summary, status);
        RefreshSetupInstallPlanItems();
    }

    private void RefreshSetupInstallPlanItems()
    {
        SetupInstallPlanItems.Clear();
        foreach (var item in BuildSetupInstallPlanItems())
        {
            SetupInstallPlanItems.Add(item);
        }
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

    private static string BuildAsrCudaRuntimeSetupFailureMessage(string message, string failureCategory)
    {
        return failureCategory switch
        {
            AsrCudaRuntimeService.FailureCategoryConfigurationMissing =>
                $"CUDA ASR runtime source is not configured. Configure {AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable}, then retry. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryNetworkUnavailable =>
                $"CUDA ASR runtime could not be downloaded. Check the network connection or proxy settings, then retry. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryHashMismatch =>
                $"CUDA ASR runtime failed hash verification and was not installed. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryArchiveInvalid =>
                $"CUDA ASR runtime archive was not usable. It must contain cuBLAS and cuDNN DLLs for faster-whisper/CTranslate2. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryInstallFailed =>
                $"CUDA ASR runtime could not be installed under tools\\asr. Details: {message}",
            _ => message
        };
    }

    private static string BuildOptionalDiarizationRuntimeFailureMessage(string message, string failureCategory)
    {
        return $"{BuildDiarizationRuntimeSetupFailureMessage(message, failureCategory)} KoeNote本体は利用できます。話者識別は後から追加導入できます。";
    }

    private static string BuildCudaReviewRuntimeSetupFailureMessage(string message, string failureCategory)
    {
        return failureCategory switch
        {
            CudaReviewRuntimeService.FailureCategoryConfigurationMissing =>
                $"CUDA review runtime source is not configured. KoeNote will continue with CPU review runtime. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryCpuRuntimeMissing =>
                $"CUDA review runtime needs the CPU review runtime first. KoeNote will continue without CUDA. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryNetworkUnavailable =>
                $"CUDA review runtime could not be downloaded. Check the network connection or proxy settings; CPU review runtime remains available. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryHashMismatch =>
                $"CUDA review runtime failed hash verification and was not installed. CPU review runtime remains available. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryArchiveInvalid =>
                $"CUDA review runtime archive was not usable. CPU review runtime remains available. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryInstallFailed =>
                $"CUDA review runtime could not be installed. CPU review runtime remains available. Details: {message}",
            _ => message
        };
    }

    private static string BuildOptionalCudaReviewRuntimeFailureMessage(string message, string failureCategory)
    {
        return $"{BuildCudaReviewRuntimeSetupFailureMessage(message, failureCategory)} KoeNote本体は利用できます。整文はCPU版で続行できます。GPU高速化は後から追加導入できます。";
    }
}
