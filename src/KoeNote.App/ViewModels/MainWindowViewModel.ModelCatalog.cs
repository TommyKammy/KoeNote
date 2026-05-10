using System.IO;
using System.Net.Http;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task ShowModelCatalogAsync()
    {
        OpenDetailPanel(2);
        MarkInterruptedModelDownloads();
        RefreshModelCatalog();
        var installedCount = ModelCatalogEntries.Count(static entry => entry.IsInstalled);
        LatestLog = $"Model catalog loaded: {ModelCatalogEntries.Count} entries, {installedCount} installed. Select a model in the Models tab for use/license/forget actions.";
        return Task.CompletedTask;
    }

    private void MarkInterruptedModelDownloads()
    {
        if (IsModelDownloadInProgress)
        {
            return;
        }

        var interruptedCount = _modelDownloadJobRepository.MarkRunningJobsInterrupted();
        if (interruptedCount > 0)
        {
            LatestLog = $"{interruptedCount} 件の中断されたモデルダウンロードを再開可能な状態に戻しました。";
        }
    }

    private Task RegisterPreinstalledModelsAsync()
    {
        var catalog = _modelCatalogService.LoadBuiltInCatalog();
        RegisterIfPresent(catalog, "llm-jp-4-8b-thinking-q4-k-m", Paths.ReviewModelPath, "preinstalled");
        RefreshModelCatalog();
        LatestLog = "Preinstalled model scan completed.";
        return Task.CompletedTask;
    }

    private void RegisterDiscoveredManagedModels()
    {
        var catalog = _modelCatalogService.LoadBuiltInCatalog();
        foreach (var item in catalog.Models)
        {
            var existing = _installedModelRepository.FindInstalledModel(item.ModelId);
            if (existing is not null &&
                existing.Verified &&
                (File.Exists(existing.FilePath) || Directory.Exists(existing.FilePath)))
            {
                continue;
            }

            foreach (var candidatePath in GetManagedModelCandidatePaths(item))
            {
                if (!File.Exists(candidatePath) && !Directory.Exists(candidatePath))
                {
                    continue;
                }

                _modelInstallService.RegisterLocalModel(item, candidatePath, "discovered");
                break;
            }
        }
    }

    private IEnumerable<string> GetManagedModelCandidatePaths(ModelCatalogItem item)
    {
        var roots = new[]
        {
            _setupState.StorageRoot,
            Paths.DefaultModelStorageRoot,
            Paths.UserModels,
            Paths.MachineModels
        }
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(static root => root!)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            yield return _modelInstallService.GetDefaultInstallPath(item, root);
        }
    }

    private async Task DownloadSelectedModelAsync()
    {
        if (SelectedModelCatalogEntry is null)
        {
            return;
        }

        await DownloadModelAsync(SelectedModelCatalogEntry, resumeDownloadId: null);
    }

    private Task PauseSelectedModelDownloadAsync()
    {
        var job = SelectedModelCatalogEntry?.LatestDownloadJob;
        if (job is null)
        {
            return Task.CompletedTask;
        }

        _modelDownloadService.Pause(job.DownloadId);
        CancelActiveModelDownloadFor(SelectedModelCatalogEntry);
        RefreshModelCatalog();
        LatestLog = $"Model download paused: {SelectedModelCatalogEntry?.DisplayName}";
        return Task.CompletedTask;
    }

    private async Task ResumeSelectedModelDownloadAsync()
    {
        var entry = SelectedModelCatalogEntry;
        var job = entry?.LatestDownloadJob;
        if (entry is null || job is null)
        {
            return;
        }

        await DownloadModelAsync(entry, job.DownloadId);
    }

    private Task CancelSelectedModelDownloadAsync()
    {
        var job = SelectedModelCatalogEntry?.LatestDownloadJob;
        if (job is null)
        {
            return Task.CompletedTask;
        }

        _modelDownloadService.Cancel(job.DownloadId);
        CancelActiveModelDownloadFor(SelectedModelCatalogEntry);
        RefreshModelCatalog();
        LatestLog = $"Model download cancelled: {SelectedModelCatalogEntry?.DisplayName}";
        return Task.CompletedTask;
    }

    private async Task RetrySelectedModelDownloadAsync()
    {
        if (SelectedModelCatalogEntry is null)
        {
            return;
        }

        await DownloadModelAsync(SelectedModelCatalogEntry, resumeDownloadId: null);
    }

    private async Task DownloadModelAsync(ModelCatalogEntry entry, string? resumeDownloadId)
    {
        _modelDownloadCancellation?.Cancel();
        using var cancellation = new CancellationTokenSource();
        _modelDownloadCancellation = cancellation;
        _activeModelDownloadModelId = entry.ModelId;
        BeginModelDownloadProgress(entry.DisplayName);
        var progress = new Progress<ModelDownloadProgress>(downloadProgress =>
        {
            UpdateModelDownloadProgress(entry.DisplayName, downloadProgress);
            RefreshModelCatalogForDownloadProgress(downloadProgress);
        });

        try
        {
            LatestLog = $"Model download started: {entry.DisplayName}";
            if (resumeDownloadId is null)
            {
                var targetPath = _modelInstallService.GetDefaultInstallPath(entry.CatalogItem);
                await _modelDownloadService.DownloadAndInstallAsync(entry.CatalogItem, targetPath, progress, cancellation.Token);
            }
            else
            {
                await _modelDownloadService.ResumeDownloadAndInstallAsync(entry.CatalogItem, resumeDownloadId, progress, cancellation.Token);
            }

            RefreshModelCatalogKeepingSelection(entry.ModelId);
            CompleteModelDownloadProgress(entry.DisplayName, succeeded: true);
        }
        catch (OperationCanceledException)
        {
            RefreshModelCatalogKeepingSelection(entry.ModelId);
            CompleteModelDownloadProgress(entry.DisplayName, succeeded: false, "Model download cancelled.");
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException or UnauthorizedAccessException)
        {
            RefreshModelCatalogKeepingSelection(entry.ModelId);
            CompleteModelDownloadProgress(entry.DisplayName, succeeded: false, $"Model download failed: {entry.DisplayName}: {exception.Message}");
        }
        finally
        {
            if (ReferenceEquals(_modelDownloadCancellation, cancellation))
            {
                _modelDownloadCancellation = null;
                _activeModelDownloadModelId = null;
            }

            UpdateModelCatalogCommandStates();
        }
    }

    private void CancelActiveModelDownloadFor(ModelCatalogEntry? entry)
    {
        if (entry is null ||
            !string.Equals(_activeModelDownloadModelId, entry.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _modelDownloadCancellation?.Cancel();
    }

    private Task UseSelectedModelAsync()
    {
        if (SelectedModelCatalogEntry is null)
        {
            return Task.CompletedTask;
        }

        if (SelectedModelCatalogEntry.Role.Equals("asr", StringComparison.OrdinalIgnoreCase))
        {
            SelectedAsrEngineId = SelectedModelCatalogEntry.EngineId;
            LatestLog = $"ASR model selected: {SelectedModelCatalogEntry.DisplayName} ({SelectedModelCatalogEntry.EngineId})";
        }
        else
        {
            LatestLog = $"整文モデルを選択しました: {SelectedModelCatalogEntry.DisplayName}";
        }

        return Task.CompletedTask;
    }

    private Task ShowSelectedModelLicenseAsync()
    {
        if (SelectedModelCatalogEntry is not null)
        {
            LatestLog = $"""
                {_modelLicenseViewer.BuildLicenseSummary(SelectedModelCatalogEntry.ModelId)}
                Size: {SelectedModelCatalogEntry.SizeSummary}
                Requirements: {SelectedModelCatalogEntry.RuntimeRequirement}
                Install: {SelectedModelCatalogEntry.InstallState}
                Download: {SelectedModelCatalogEntry.DownloadState}
                """;
        }

        return Task.CompletedTask;
    }

    private Task DeleteSelectedModelFilesAsync()
    {
        var entry = SelectedModelCatalogEntry;
        if (entry is null || !entry.IsInstalled)
        {
            return Task.CompletedTask;
        }

        var installedPath = entry.InstalledModel?.FilePath ?? string.Empty;
        var sizeSummary = entry.SizeSummary;
        if (!ConfirmAction(
            "モデルファイルの削除",
            $"「{entry.DisplayName}」のモデルファイルを削除します。\n\n対象: {installedPath}\n容量: {sizeSummary}\n\nDBの導入済み登録とダウンロード履歴も削除されます。この操作は元に戻せません。"))
        {
            return Task.CompletedTask;
        }

        try
        {
            var result = _modelInstallService.DeleteModelFiles(entry.ModelId);
            var deletedDownloadJobs = _modelDownloadJobRepository.DeleteForModel(entry.ModelId);
            LatestLog = $"Deleted model files: {entry.DisplayName} ({FormatByteSize(result.DeletedBytes)}, download records {deletedDownloadJobs})";
            RefreshModelCatalog();
        }
        catch (InvalidOperationException exception)
        {
            LatestLog = $"Model file delete skipped: {exception.Message}";
        }
        catch (IOException exception)
        {
            LatestLog = $"Model file delete failed: {entry.DisplayName}: {exception.Message}";
        }
        catch (UnauthorizedAccessException exception)
        {
            LatestLog = $"Model file delete failed: {entry.DisplayName}: {exception.Message}";
        }

        return Task.CompletedTask;
    }

    private void RefreshModelCatalog()
    {
        var selectedModelId = SelectedModelCatalogEntry?.ModelId;
        ModelCatalogEntries.Clear();
        foreach (var entry in _modelCatalogService.ListEntries())
        {
            ModelCatalogEntries.Add(entry);
        }

        SelectedModelCatalogEntry = selectedModelId is null
            ? ModelCatalogEntries.FirstOrDefault()
            : ModelCatalogEntries.FirstOrDefault(entry =>
                entry.ModelId.Equals(selectedModelId, StringComparison.OrdinalIgnoreCase)) ?? ModelCatalogEntries.FirstOrDefault();
        RefreshAvailableAsrEngines();
        OnPropertyChanged(nameof(RequiredRuntimeAssetsReady));
        OnPropertyChanged(nameof(ReviewStageAssetsReady));
        OnPropertyChanged(nameof(SummaryStageAssetsReady));
        OnPropertyChanged(nameof(AsrModel));
        OnPropertyChanged(nameof(ReviewModel));
        RefreshLlmSettingsDisplay();
        OnPropertyChanged(nameof(CanRunSelectedJob));
        OnPropertyChanged(nameof(RunPreflightSummary));
        OnPropertyChanged(nameof(RunPreflightDetail));
        UpdateModelCatalogCommandStates();
    }

    private void RefreshModelCatalogKeepingSelection(string modelId)
    {
        RefreshModelCatalog();
        SelectedModelCatalogEntry = ModelCatalogEntries.FirstOrDefault(entry =>
            entry.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase)) ?? SelectedModelCatalogEntry;
    }

    private void RegisterIfPresent(ModelCatalog catalog, string modelId, string path, string sourceType)
    {
        var item = catalog.Models.FirstOrDefault(model => string.Equals(model.ModelId, modelId, StringComparison.OrdinalIgnoreCase));
        if (item is not null && (File.Exists(path) || Directory.Exists(path)))
        {
            _modelInstallService.RegisterLocalModel(item, path, sourceType);
        }
    }

    private ModelCatalogEntry? FindInstalledCatalogEntry(string role, Func<ModelCatalogEntry, bool> predicate)
    {
        return FindInstalledCatalogEntry(ModelCatalogEntries, role, predicate) ??
            FindInstalledCatalogEntry(_modelCatalogService.ListEntries(), role, predicate);
    }

    private static ModelCatalogEntry? FindInstalledCatalogEntry(
        IEnumerable<ModelCatalogEntry> entries,
        string role,
        Func<ModelCatalogEntry, bool> predicate)
    {
        return entries.FirstOrDefault(entry =>
            entry.IsInstalled &&
            entry.IsVerified &&
            entry.InstalledModel is { } installed &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)) &&
            entry.Role.Equals(role, StringComparison.OrdinalIgnoreCase) &&
            predicate(entry));
    }

    private bool IsSelectedAsrEngineReady()
    {
        return SelectedAsrEngineId switch
        {
            "kotoba-whisper-v2.2-faster" => File.Exists(Paths.FasterWhisperScriptPath) &&
                FasterWhisperRuntimeLayout.HasPackage(Paths) &&
                ModelPathExists("kotoba-whisper-v2.2-faster", Paths.KotobaWhisperFasterModelPath),
            "whisper-base" => File.Exists(Paths.FasterWhisperScriptPath) &&
                FasterWhisperRuntimeLayout.HasPackage(Paths) &&
                ModelPathExists("whisper-base", Paths.WhisperBaseModelPath),
            "whisper-small" => File.Exists(Paths.FasterWhisperScriptPath) &&
                FasterWhisperRuntimeLayout.HasPackage(Paths) &&
                ModelPathExists("whisper-small", Paths.WhisperSmallModelPath),
            "faster-whisper-large-v3-turbo" => File.Exists(Paths.FasterWhisperScriptPath) &&
                FasterWhisperRuntimeLayout.HasPackage(Paths) &&
                ModelPathExists("faster-whisper-large-v3-turbo", Paths.FasterWhisperModelPath),
            "faster-whisper-large-v3" => File.Exists(Paths.FasterWhisperScriptPath) &&
                FasterWhisperRuntimeLayout.HasPackage(Paths) &&
                ModelPathExists("faster-whisper-large-v3", Paths.FasterWhisperLargeV3ModelPath),
            "reazonspeech-k2-v3" => File.Exists(Paths.ReazonSpeechK2ScriptPath) &&
                ModelPathExists("reazonspeech-k2-v3-ja", Paths.ReazonSpeechK2ModelPath),
            _ => false
        };
    }

    private static bool IsUserSelectableAsrEngine(string? engineId)
    {
        return engineId is "kotoba-whisper-v2.2-faster"
            or "whisper-base"
            or "whisper-small"
            or "faster-whisper-large-v3-turbo"
            or "faster-whisper-large-v3";
    }

    private string ResolveInitialAsrEngineId(string? savedEngineId)
    {
        if (IsUserSelectableAsrEngine(savedEngineId))
        {
            return savedEngineId!;
        }

        if (IsUserSelectableAsrEngine(_setupState.SelectedAsrModelId))
        {
            return _setupState.SelectedAsrModelId!;
        }

        foreach (var candidate in new[]
        {
            "faster-whisper-large-v3-turbo",
            "whisper-base",
            "whisper-small",
            "kotoba-whisper-v2.2-faster",
            "faster-whisper-large-v3"
        })
        {
            var installed = _installedModelRepository.FindInstalledModel(candidate);
            if (installed is not null &&
                installed.Verified &&
                (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
            {
                return candidate;
            }
        }

        return DefaultSelectableAsrEngineId;
    }

    private void RefreshAvailableAsrEngines()
    {
        var selectedEngineId = SelectedAsrEngineId;
        AvailableAsrEngines.Clear();
        foreach (var engine in _asrEngineRegistry.Engines.Where(static engine => IsUserSelectableAsrEngine(engine.EngineId)))
        {
            AvailableAsrEngines.Add(new AsrEngineOption(
                engine.EngineId,
                engine.DisplayName,
                IsAsrEngineInstalled(engine.EngineId)));
        }

        if (!string.IsNullOrWhiteSpace(selectedEngineId))
        {
            _selectedAsrEngineId = selectedEngineId;
        }

        UpdateSelectedSettingsAsrEngine();
        OnPropertyChanged(nameof(SelectedAsrEngineId));
    }

    private void UpdateSelectedSettingsAsrEngine()
    {
        var selected = AvailableAsrEngines.FirstOrDefault(engine =>
            engine.EngineId.Equals(_selectedAsrEngineId, StringComparison.OrdinalIgnoreCase));
        if (!ReferenceEquals(_selectedSettingsAsrEngine, selected))
        {
            _selectedSettingsAsrEngine = selected;
            OnPropertyChanged(nameof(SelectedSettingsAsrEngine));
        }
    }

    private bool IsAsrEngineInstalled(string engineId)
    {
        return FindInstalledCatalogEntry(
            "asr",
            entry => string.Equals(entry.EngineId, engineId, StringComparison.OrdinalIgnoreCase)) is not null;
    }

    private bool IsReviewModelReady()
    {
        var modelId = ResolveEffectiveReviewModelId();

        if (modelId.Equals("llm-jp-4-8b-thinking-q4-k-m", StringComparison.OrdinalIgnoreCase))
        {
            return ModelPathExists(modelId, Paths.ReviewModelPath);
        }

        var installed = _installedModelRepository.FindInstalledModel(modelId);
        return installed is not null &&
            installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath));
    }

    private bool IsSelectedReviewRuntimeReady()
    {
        return File.Exists(GetSelectedReviewRuntimePath());
    }

    private string GetSelectedReviewRuntimePath()
    {
        var catalog = _modelCatalogService.LoadBuiltInCatalog();
        var modelId = ResolveEffectiveReviewModelId(catalog);
        return ReviewRuntimeResolver.ResolveLlamaCompletionPath(Paths, catalog, modelId);
    }

    private string ResolveEffectiveReviewModelId(ModelCatalog? catalog = null)
    {
        catalog ??= _modelCatalogService.LoadBuiltInCatalog();

        if (IsSelectableReviewModel(catalog, _setupState.SelectedReviewModelId))
        {
            return _setupState.SelectedReviewModelId!;
        }

        var presetReviewModelId = (catalog.Presets ?? [])
            .FirstOrDefault(preset =>
                !string.IsNullOrWhiteSpace(_setupState.SelectedModelPresetId) &&
                preset.PresetId.Equals(_setupState.SelectedModelPresetId, StringComparison.OrdinalIgnoreCase))
            ?.ReviewModelId;

        return IsSelectableReviewModel(catalog, presetReviewModelId)
            ? presetReviewModelId!
            : "llm-jp-4-8b-thinking-q4-k-m";
    }

    private static bool IsSelectableReviewModel(ModelCatalog catalog, string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            catalog.Models.Any(model =>
                model.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
                model.ModelId.Equals(modelId, StringComparison.OrdinalIgnoreCase) &&
                ModelCatalogCompatibility.IsSelectable(model));
    }

    private bool ModelPathExists(string modelId, string fallbackPath)
    {
        var installed = _installedModelRepository.FindInstalledModel(modelId);
        if (installed is not null &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath)))
        {
            return true;
        }

        return File.Exists(fallbackPath) || Directory.Exists(fallbackPath);
    }

    private void UpdateModelCatalogCommandStates()
    {
        if (DownloadSelectedModelCommand is RelayCommand downloadCommand)
        {
            downloadCommand.RaiseCanExecuteChanged();
        }

        if (PauseSelectedModelDownloadCommand is RelayCommand pauseCommand)
        {
            pauseCommand.RaiseCanExecuteChanged();
        }

        if (ResumeSelectedModelDownloadCommand is RelayCommand resumeCommand)
        {
            resumeCommand.RaiseCanExecuteChanged();
        }

        if (CancelSelectedModelDownloadCommand is RelayCommand cancelCommand)
        {
            cancelCommand.RaiseCanExecuteChanged();
        }

        if (RetrySelectedModelDownloadCommand is RelayCommand retryCommand)
        {
            retryCommand.RaiseCanExecuteChanged();
        }

        if (UseSelectedModelCommand is RelayCommand useCommand)
        {
            useCommand.RaiseCanExecuteChanged();
        }

        if (ShowSelectedModelLicenseCommand is RelayCommand licenseCommand)
        {
            licenseCommand.RaiseCanExecuteChanged();
        }

        if (DeleteSelectedModelFilesCommand is RelayCommand deleteFilesCommand)
        {
            deleteFilesCommand.RaiseCanExecuteChanged();
        }
    }

    private bool CanDownloadSelectedModel()
    {
        return SelectedModelCatalogEntry is { IsInstalled: false } entry &&
            entry.IsDirectDownloadSupported &&
            !IsDownloadRunning(entry.LatestDownloadJob);
    }

    private bool CanPauseSelectedModelDownload()
    {
        return IsDownloadRunning(SelectedModelCatalogEntry?.LatestDownloadJob);
    }

    private bool CanResumeSelectedModelDownload()
    {
        return SelectedModelCatalogEntry?.LatestDownloadJob is { Status: "paused" };
    }

    private bool CanCancelSelectedModelDownload()
    {
        return SelectedModelCatalogEntry?.LatestDownloadJob is { Status: "running" or "paused" };
    }

    private bool CanRetrySelectedModelDownload()
    {
        return SelectedModelCatalogEntry is { IsInstalled: false, LatestDownloadJob.Status: "failed" or "cancelled" } entry &&
            entry.IsDirectDownloadSupported;
    }

    private bool CanDeleteSelectedModelFiles()
    {
        return SelectedModelCatalogEntry is { IsInstalled: true } entry &&
            !IsRunInProgress &&
            !IsModelDownloadInProgress &&
            !IsDownloadRunning(entry.LatestDownloadJob);
    }

    private static bool IsDownloadRunning(ModelDownloadJob? job)
    {
        return string.Equals(job?.Status, "running", StringComparison.OrdinalIgnoreCase);
    }
}
