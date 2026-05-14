using System.IO;
using KoeNote.App.Services;
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
        CommitSetupSelectionDraft();

        if (_setupState.IsCompleted && SelectedSetupConfigurationReady)
        {
            IsSetupWizardModalOpen = false;
            LatestLog = "Setup is complete. KoeNote is ready.";
            return Task.CompletedTask;
        }

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
            return SelectedSetupConfigurationReady || CanInstallSelectedPreset();
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
        var recommendation = _setupWizardService.GetPresetRecommendation();
        ApplySetupModelPresetDraft(recommendation.PresetId);
        LatestLog = "Recommended setup choices selected. Accept licenses before installation.";
        return Task.CompletedTask;
    }

    private void ApplySetupModelPresetDraft(string presetId)
    {
        try
        {
            var draft = CreateSetupPresetDraft(_setupSelectionDraft ?? _setupState, presetId);
            _setupSelectionDraft = draft;
            _selectedSetupAsrModel = SetupAsrModelChoices.FirstOrDefault(entry =>
                entry.ModelId.Equals(draft.SelectedAsrModelId, StringComparison.OrdinalIgnoreCase));
            _selectedSetupReviewModel = SetupReviewModelChoices.FirstOrDefault(entry =>
                entry.ModelId.Equals(draft.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase));
            _selectedSetupModelPreset = SetupModelPresetChoices.FirstOrDefault(preset =>
                preset.PresetId.Equals(draft.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase));

            RefreshSetupSelectionPreview();
            LatestLog = $"Setup model preset drafted: {presetId}";
        }
        catch (InvalidOperationException ex)
        {
            LatestLog = $"Setup preset selection failed: {ex.Message}";
        }
    }

    private Task SetupAcceptLicensesAsync()
    {
        CommitSetupSelectionDraft();
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

        CommitSetupSelectionDraft();

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
                ModelDownloadProgressStageText = "確認中";
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
                ModelDownloadProgressStageText = "インストール中";
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

            if (!SetupReviewRuntimeReady)
            {
                const string message = "CPU版Review runtimeが見つかりません。tools\\review\\llama-completion.exe を含むKoeNote Core runtimeを配置してから、もう一度セットアップを実行してください。";
                SetSetupInstallStatus("Review runtime", "失敗", message);
                CompleteModelDownloadProgress(displayName, succeeded: false, message);
                LatestLog = message;
                return;
            }

            SetSetupInstallStatus("Review runtime", "確認済み", Paths.LlamaCompletionPath);

            if (SetupAsrCudaRuntimeRecommended && !SetupAsrCudaRuntimeReady)
            {
                SetSetupInstallStatus("ASR GPU runtime", "導入中", "NVIDIA GPU向けのASR CUDA runtime");
                ModelDownloadProgressSummary = $"Installing {displayName}: preparing NVIDIA CUDA/cuDNN runtime for selected ASR model...";
                ModelDownloadProgressStageText = "確認中";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallAsrCudaRuntimeAsync(
                    cancellation.Token,
                    CreateRuntimeInstallProgress());
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = BuildAsrCudaRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory);
                    var fallbackMessage = $"{message} ASR GPU acceleration was not installed, but CPU ASR remains available. You can retry ASR GPU runtime installation later from Setup.";
                    SetSetupInstallStatus("ASR GPU runtime", "未導入", fallbackMessage);
                    SetupAsrCudaRuntimeSummary = fallbackMessage;
                    LatestLog = fallbackMessage;
                }
                else
                {
                    SetupAsrCudaRuntimeSummary = $"CUDA ASR runtime installed: {runtimeResult.InstallPath}";
                    SetSetupInstallStatus("ASR GPU runtime", "完了", runtimeResult.InstallPath);
                    LatestLog = runtimeResult.Message;
                }
            }
            else if (SetupAsrCudaRuntimeRecommended)
            {
                SetSetupInstallStatus("ASR GPU runtime", "スキップ", "導入済みです");
            }

            if (!SetupDiarizationRuntimeReady)
            {
                SetSetupInstallStatus("話者識別", "導入中", "話者分離を使うための必須runtime");
                ModelDownloadProgressSummary = $"Installing {displayName}: checking bundled Python and pip for speaker diarization...";
                ModelDownloadProgressStageText = "確認中";
                IsModelDownloadProgressIndeterminate = true;
                var preflight = await _setupWizardService.CheckDiarizationRuntimeInstallPreflightAsync(cancellation.Token);
                if (!preflight.IsReady)
                {
                    var message = AppendFailureCategory(
                        BuildDiarizationRuntimeSetupFailureMessage(preflight.Message, preflight.FailureCategory),
                        preflight.FailureCategory);
                    SetSetupInstallStatus("話者識別", "失敗", message);
                    SetupDiarizationRuntimeSummary = message;
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
                }
                else
                {
                    ModelDownloadProgressSummary = $"Installing {displayName}: installing speaker diarization runtime with bundled Python...";
                    ModelDownloadProgressStageText = "インストール中";
                    var runtimeResult = await _setupWizardService.InstallDiarizationRuntimeAsync(cancellation.Token);
                    RefreshSetupWizard();
                    if (!runtimeResult.IsSucceeded)
                    {
                        var message = AppendFailureCategory(
                            BuildDiarizationRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory),
                            runtimeResult.FailureCategory);
                        SetSetupInstallStatus("話者識別", "失敗", message);
                        SetupDiarizationRuntimeSummary = message;
                        CompleteModelDownloadProgress(displayName, succeeded: false, message);
                        LatestLog = message;
                        return;
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
                ModelDownloadProgressSummary = $"Installing {displayName}: preparing NVIDIA CUDA runtime for detected NVIDIA GPU...";
                ModelDownloadProgressStageText = "確認中";
                IsModelDownloadProgressIndeterminate = true;
                var runtimeResult = await _setupWizardService.InstallCudaReviewRuntimeAsync(
                    cancellation.Token,
                    CreateRuntimeInstallProgress());
                RefreshSetupWizard();
                if (!runtimeResult.IsSucceeded)
                {
                    var message = AppendFailureCategory(
                        BuildCudaReviewRuntimeSetupFailureMessage(runtimeResult.Message, runtimeResult.FailureCategory),
                        runtimeResult.FailureCategory);
                    SetSetupInstallStatus("GPU高速化", "失敗", message);
                    SetupCudaReviewRuntimeSummary = message;
                    CompleteModelDownloadProgress(displayName, succeeded: false, message);
                    LatestLog = message;
                    return;
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
                ModelDownloadProgressStageText = "ダウンロード中";
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
            ModelDownloadProgressSummary = "導入が完了しました。最終確認へ進めます。";
            ModelDownloadNotification = "導入が完了しました。KoeNoteを使う準備に進めます。";

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
        CommitSetupSelectionDraft();
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
        CommitSetupSelectionDraft();
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
        SetSetupInstallStatus("話者識別", "導入中", "話者分離を使うための必須runtime");
        ModelDownloadProgressSummary = "Checking bundled Python and pip for diarize runtime...";
        ModelDownloadProgressStageText = "確認中";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var preflight = await _setupWizardService.CheckDiarizationRuntimeInstallPreflightAsync();
            if (!preflight.IsReady)
            {
                var preflightMessage = AppendFailureCategory(
                    BuildDiarizationRuntimeSetupFailureMessage(preflight.Message, preflight.FailureCategory),
                    preflight.FailureCategory);
                RefreshSetupWizard();
                SetSetupInstallStatus("話者識別", "失敗", preflightMessage);
                SetupDiarizationRuntimeSummary = preflightMessage;
                CompleteModelDownloadProgress(displayName, succeeded: false, preflightMessage);
                LatestLog = preflightMessage;
                return;
            }

            ModelDownloadProgressSummary = "Installing diarize runtime with bundled Python...";
            ModelDownloadProgressStageText = "インストール中";
            var result = await _setupWizardService.InstallDiarizationRuntimeAsync();
            RefreshSetupWizard();
            var message = result.IsSucceeded
                ? $"Speaker diarization runtime installed: {result.InstallPath}"
                : AppendFailureCategory(
                    BuildDiarizationRuntimeSetupFailureMessage(result.Message, result.FailureCategory),
                    result.FailureCategory);
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
            const string message = "話者識別runtimeの導入を中止しました。Wizardの一括導入から再試行してください。";
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
        ModelDownloadProgressSummary = "Installing CUDA review runtime: preparing NVIDIA CUDA redist...";
        ModelDownloadProgressStageText = "確認中";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var result = await _setupWizardService.InstallCudaReviewRuntimeAsync(progress: CreateRuntimeInstallProgress());
            RefreshSetupWizard();
            var message = result.IsSucceeded
                ? $"CUDA review runtime installed: {result.InstallPath}"
                : AppendFailureCategory(
                    BuildCudaReviewRuntimeSetupFailureMessage(result.Message, result.FailureCategory),
                    result.FailureCategory);
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
            const string message = "GPU高速化runtimeの導入を中止しました。Wizardの一括導入から再試行してください。";
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
        ModelDownloadProgressSummary = "Installing CUDA ASR runtime: preparing NVIDIA CUDA/cuDNN redist...";
        ModelDownloadProgressStageText = "確認中";
        IsModelDownloadProgressIndeterminate = true;

        try
        {
            var result = await _setupWizardService.InstallAsrCudaRuntimeAsync(progress: CreateRuntimeInstallProgress());
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

    private IProgress<RuntimeInstallProgress> CreateRuntimeInstallProgress()
    {
        return new Progress<RuntimeInstallProgress>(progress =>
        {
            ModelDownloadProgressStageText = progress.StageText;
            ModelDownloadProgressSummary = progress.Message;
            LatestLog = progress.Message;
        });
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
        if (result.IsSucceeded)
        {
            _setupState = _setupWizardService.CompleteIfReady();
        }

        RefreshSetupWizard(refreshSmokeChecks: false);
        LatestLog = result.IsSucceeded
            ? $"最終確認に成功しました。Report: {result.ReportPath}"
            : $"最終確認で不足項目が見つかりました。Report: {result.ReportPath}";
        return Task.CompletedTask;
    }

    private Task SetupCompleteAsync()
    {
        CommitSetupSelectionDraft();
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

    private void ApplySetupModelSelectionDraft(string role, string modelId)
    {
        try
        {
            var selected = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
                ? SetupAsrModelChoices.FirstOrDefault(entry =>
                    entry.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase))
                : SetupReviewModelChoices.FirstOrDefault(entry =>
                    entry.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase));
            if (selected is null)
            {
                throw new InvalidOperationException($"Model is not selectable in setup: {modelId}");
            }

            var current = _setupSelectionDraft ?? _setupState;
            _setupSelectionDraft = role.Equals("asr", StringComparison.OrdinalIgnoreCase)
                ? current with
                {
                    IsCompleted = false,
                    LastSmokeSucceeded = false,
                    CurrentStep = SetupStep.AsrModel,
                    SetupMode = "custom",
                    SelectedModelPresetId = null,
                    SelectedAsrModelId = selected.ModelId
                }
                : current with
                {
                    IsCompleted = false,
                    LastSmokeSucceeded = false,
                    CurrentStep = SetupStep.ReviewModel,
                    SetupMode = "custom",
                    SelectedModelPresetId = null,
                    SelectedReviewModelId = selected.ModelId
                };
            _selectedSetupModelPreset = null;
            if (role.Equals("asr", StringComparison.OrdinalIgnoreCase))
            {
                _selectedSetupAsrModel = selected;
            }
            else
            {
                _selectedSetupReviewModel = selected;
            }

            RefreshSetupSelectionPreview();
            LatestLog = $"Setup {role} model drafted: {modelId}";
        }
        catch (InvalidOperationException ex)
        {
            LatestLog = $"Setup selection failed: {ex.Message}";
        }
    }

    private void ApplySettingsReviewModelSelection(string modelId)
    {
        try
        {
            _setupSelectionDraft = null;
            _setupState = _setupWizardService.SelectModel("review", modelId);
            RefreshSetupWizard();
            RefreshLlmSettingsDisplay(synchronizeFromSetup: true);
            _ = SelectActiveReadablePolishingPromptModelFamily();
            LatestLog = $"Settings review model selected: {modelId}";
        }
        catch (InvalidOperationException ex)
        {
            LatestLog = $"Settings review model selection failed: {ex.Message}";
        }
    }

    private void RefreshSetupWizard(bool refreshSmokeChecks = true)
    {
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

        var displayState = _setupSelectionDraft ?? CreateAutomaticRecommendationDraft(_setupState);
        _selectedSetupAsrModel = SetupAsrModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(displayState.SelectedAsrModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupAsrModelChoices.FirstOrDefault();
        _selectedSetupReviewModel = SetupReviewModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(displayState.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupReviewModelChoices.FirstOrDefault();
        _selectedSettingsReviewModel = SetupReviewModelChoices.FirstOrDefault(entry =>
            entry.ModelId.Equals(_setupState.SelectedReviewModelId, StringComparison.OrdinalIgnoreCase)) ??
            SetupReviewModelChoices.FirstOrDefault();
        _selectedSetupModelPreset = SetupModelPresetChoices.FirstOrDefault(preset =>
            preset.PresetId.Equals(displayState.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase));
        RefreshAvailableAsrEngines();

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
        OnPropertyChanged(nameof(SelectedSettingsReviewModel));
        OnPropertyChanged(nameof(SelectedSetupModelPreset));
        OnPropertyChanged(nameof(AsrModel));
        OnPropertyChanged(nameof(ReviewModel));
        OnPropertyChanged(nameof(SelectedSetupModelPresetDescription));
        OnPropertyChanged(nameof(SelectedSetupModelPresetModels));
        OnPropertyChanged(nameof(SelectedSetupModelsReady));
        OnPropertyChanged(nameof(SetupFasterWhisperRuntimeReady));
        OnPropertyChanged(nameof(SetupReviewRuntimeReady));
        OnPropertyChanged(nameof(SetupDiarizationRuntimeReady));
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeRecommended));
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeReady));
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeActionText));
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeRecommended));
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeReady));
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeActionText));
        OnPropertyChanged(nameof(SetupTernaryReviewRuntimeReady));
        OnPropertyChanged(nameof(SetupRequiredRuntimeReady));
        OnPropertyChanged(nameof(SetupConditionalRuntimeReady));
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
        OnPropertyChanged(nameof(SummaryStageAssetsReady));
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

    private SetupState CreateAutomaticRecommendationDraft(SetupState state)
    {
        if (state.IsCompleted ||
            state.CurrentStep is not (SetupStep.Welcome or SetupStep.SetupMode) ||
            !string.Equals(state.SelectedModelPresetId, "recommended", StringComparison.OrdinalIgnoreCase))
        {
            return state;
        }

        var presetId = _setupPresetRecommendation?.PresetId;
        if (string.IsNullOrWhiteSpace(presetId) ||
            presetId.Equals(state.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase) ||
            !SetupModelPresetChoices.Any(preset => preset.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase)))
        {
            return state;
        }

        _setupSelectionDraft = CreateSetupPresetDraft(state, presetId);
        return _setupSelectionDraft;
    }

    private SetupState CreateSetupPresetDraft(SetupState state, string presetId)
    {
        var preset = SetupModelPresetChoices.FirstOrDefault(candidate =>
            candidate.PresetId.Equals(presetId, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            throw new InvalidOperationException($"Model preset is not in the catalog: {presetId}");
        }

        if (!SetupAsrModelChoices.Any(entry => entry.ModelId.Equals(preset.AsrModelId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Preset references missing asr model: {preset.AsrModelId}");
        }

        if (!SetupReviewModelChoices.Any(entry => entry.ModelId.Equals(preset.ReviewModelId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Preset references missing review model: {preset.ReviewModelId}");
        }

        return state with
        {
            IsCompleted = false,
            LastSmokeSucceeded = false,
            SetupMode = preset.PresetId,
            SelectedModelPresetId = preset.PresetId,
            SelectedAsrModelId = preset.AsrModelId,
            SelectedReviewModelId = preset.ReviewModelId,
            StorageRoot = string.IsNullOrWhiteSpace(state.StorageRoot)
                ? Paths.DefaultModelStorageRoot
                : state.StorageRoot
        };
    }

    private void CommitSetupSelectionDraft()
    {
        if (_setupSelectionDraft is null)
        {
            return;
        }

        _setupState = _setupWizardService.CommitSelectionDraft(_setupSelectionDraft);
        _setupSelectionDraft = null;
        if (!string.IsNullOrWhiteSpace(_setupState.SelectedAsrModelId))
        {
            SelectedAsrEngineId = _setupState.SelectedAsrModelId;
        }

        RefreshLlmSettingsDisplay(synchronizeFromSetup: true);
    }

    private void RefreshSetupSelectionPreview()
    {
        OnPropertyChanged(nameof(SelectedSetupAsrModel));
        OnPropertyChanged(nameof(SelectedSetupReviewModel));
        OnPropertyChanged(nameof(SelectedSettingsReviewModel));
        OnPropertyChanged(nameof(SelectedSetupModelPreset));
        OnPropertyChanged(nameof(SelectedSetupModelPresetDescription));
        OnPropertyChanged(nameof(SelectedSetupModelPresetModels));
        OnPropertyChanged(nameof(SelectedSetupModelsReady));
        OnPropertyChanged(nameof(SetupMode));
        OnPropertyChanged(nameof(SetupStorageRoot));
        OnPropertyChanged(nameof(SetupReviewRuntimeReady));
        OnPropertyChanged(nameof(SetupTernaryReviewRuntimeReady));
        OnPropertyChanged(nameof(SetupRequiredRuntimeReady));
        OnPropertyChanged(nameof(SetupConditionalRuntimeReady));
        OnPropertyChanged(nameof(SelectedSetupConfigurationReady));
        OnPropertyChanged(nameof(SetupPrimaryInstallActionText));
        OnPropertyChanged(nameof(SetupPrimaryInstallSummary));
        OnPropertyChanged(nameof(SetupNextActionText));
        RefreshCudaReviewRuntimeSummary();
        OnPropertyChanged(nameof(SetupCudaReviewRuntimeSummary));
        RefreshAsrCudaRuntimeSummary();
        OnPropertyChanged(nameof(SetupAsrCudaRuntimeSummary));
        RefreshSetupInstallPlanItems();
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
            ? $"NVIDIA GPU detected. KoeNote GPU bridge is bundled; Setup Wizard will download NVIDIA CUDA redist DLLs if needed: {Paths.ReviewRuntimeDirectory}"
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
            ? $"NVIDIA GPU detected. KoeNote ASR GPU files are bundled; Setup Wizard will download NVIDIA CUDA/cuDNN redist DLLs if needed: {Paths.AsrRuntimeDirectory}"
            : "CUDA ASR runtime is not required for the selected ASR model.";
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
                "Review runtime",
                "整文と要約に必要なCPU版Review runtime",
                SetupReviewRuntimeReady ? "導入済み" : "同梱runtimeが必要です"),
            new(
                "話者識別",
                "話者分離を使うための必須runtime",
                SetupDiarizationRuntimeReady ? "導入済み" : "導入します")
        };

        if (SetupAsrCudaRuntimeRecommended)
        {
            items.Add(new(
                "ASR GPU runtime",
                "NVIDIA GPU向けのASR CUDA runtime",
                SetupAsrCudaRuntimeReady ? "導入済み" : "任意"));
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
            DiarizationRuntimeService.FailureCategoryPackageDataMissing =>
                $"diarize was installed, but required runtime data is missing. Reinstall speaker diarization runtime. Details: {message}",
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
                $"CUDA ASR runtime source is not configured. Configure NVIDIA CUDA/cuDNN redist sources, then retry. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryBundledRuntimeMissing =>
                $"CUDA ASR runtime needs the bundled KoeNote ASR GPU files first. CPU ASR remains available where supported. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryNetworkUnavailable =>
                $"CUDA ASR runtime could not download NVIDIA CUDA/cuDNN redist files. Check the network connection or proxy settings, then retry. CPU ASR fallback remains available where supported. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryHashMismatch =>
                $"CUDA ASR runtime failed NVIDIA redist hash verification and was not installed. CPU ASR fallback remains available where supported. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryArchiveInvalid =>
                $"CUDA ASR runtime archive was not usable. Setup Wizard needs NVIDIA CUDA/cuDNN DLLs for faster-whisper/CTranslate2. CPU ASR fallback remains available where supported. Details: {message}",
            AsrCudaRuntimeService.FailureCategoryInstallFailed =>
                $"CUDA ASR runtime could not be installed under tools\\asr. Details: {message}",
            _ => message
        };
    }

    private static string BuildCudaReviewRuntimeSetupFailureMessage(string message, string failureCategory)
    {
        return failureCategory switch
        {
            CudaReviewRuntimeService.FailureCategoryConfigurationMissing =>
                $"CUDA review runtime source is not configured. Setup cannot continue until this runtime is installed. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryCpuRuntimeMissing =>
                $"CUDA review runtime needs the CPU review runtime first. Setup cannot continue until this runtime is installed. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryBundledRuntimeMissing =>
                $"CUDA review runtime needs the bundled KoeNote GPU bridge first. Setup cannot continue until this runtime is installed. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryNetworkUnavailable =>
                $"CUDA review runtime could not download NVIDIA CUDA redist files. Check the network connection or proxy settings, then retry. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryHashMismatch =>
                $"CUDA review runtime failed NVIDIA redist hash verification and was not installed. Setup cannot continue until this runtime is installed. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryArchiveInvalid =>
                $"CUDA review runtime archive was not usable. Setup cannot continue until this runtime is installed. Details: {message}",
            CudaReviewRuntimeService.FailureCategoryInstallFailed =>
                $"CUDA review runtime could not be installed. Setup cannot continue until this runtime is installed. Details: {message}",
            _ => message
        };
    }

    private static string AppendFailureCategory(string message, string failureCategory)
    {
        return string.IsNullOrWhiteSpace(failureCategory)
            ? message
            : $"{message} Failure category: {failureCategory}";
    }
}
