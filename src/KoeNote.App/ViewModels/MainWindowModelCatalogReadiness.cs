using System.IO;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

namespace KoeNote.App.ViewModels;

internal static class MainWindowModelCatalogReadiness
{
    private static readonly string[] InitialAsrEngineCandidates =
    [
        "faster-whisper-large-v3-turbo",
        "whisper-base",
        "whisper-small",
        "kotoba-whisper-v2.2-faster",
        "faster-whisper-large-v3"
    ];

    public static bool IsSelectedAsrEngineReady(
        string selectedAsrEngineId,
        AppPaths paths,
        Func<string, InstalledModel?> findInstalledModel)
    {
        return selectedAsrEngineId switch
        {
            "kotoba-whisper-v2.2-faster" => IsFasterWhisperRuntimeReady(paths) &&
                ModelPathExists("kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath, findInstalledModel),
            "whisper-base" => IsFasterWhisperRuntimeReady(paths) &&
                ModelPathExists("whisper-base", paths.WhisperBaseModelPath, findInstalledModel),
            "whisper-small" => IsFasterWhisperRuntimeReady(paths) &&
                ModelPathExists("whisper-small", paths.WhisperSmallModelPath, findInstalledModel),
            "faster-whisper-large-v3-turbo" => IsFasterWhisperRuntimeReady(paths) &&
                ModelPathExists("faster-whisper-large-v3-turbo", paths.FasterWhisperModelPath, findInstalledModel),
            "faster-whisper-large-v3" => IsFasterWhisperRuntimeReady(paths) &&
                ModelPathExists("faster-whisper-large-v3", paths.FasterWhisperLargeV3ModelPath, findInstalledModel),
            "reazonspeech-k2-v3" => File.Exists(paths.ReazonSpeechK2ScriptPath) &&
                ModelPathExists("reazonspeech-k2-v3-ja", paths.ReazonSpeechK2ModelPath, findInstalledModel),
            _ => false
        };
    }

    public static string ResolveInitialAsrEngineId(
        string? savedEngineId,
        string? selectedSetupAsrModelId,
        Func<string, InstalledModel?> findInstalledModel,
        string defaultEngineId)
    {
        if (ModelCatalogPresenter.IsUserSelectableAsrEngine(savedEngineId))
        {
            return savedEngineId!;
        }

        if (ModelCatalogPresenter.IsUserSelectableAsrEngine(selectedSetupAsrModelId))
        {
            return selectedSetupAsrModelId!;
        }

        foreach (var candidate in InitialAsrEngineCandidates)
        {
            if (InstalledPathExists(findInstalledModel(candidate)))
            {
                return candidate;
            }
        }

        return defaultEngineId;
    }

    public static bool IsReviewModelReady(
        string modelId,
        AppPaths paths,
        Func<string, InstalledModel?> findInstalledModel,
        string? storageRoot = null)
    {
        if (modelId.Equals(ReviewModelSelectionResolver.LegacyReviewModelId, StringComparison.OrdinalIgnoreCase))
        {
            return ModelPathExists(modelId, paths.ReviewModelPath, findInstalledModel);
        }

        var installed = findInstalledModel(modelId);
        var primaryReady = installed is not null &&
            installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            InstalledPathExists(installed);
        return primaryReady &&
            IsDirectLlmStageFallbackReady(modelId, findInstalledModel) &&
            IsGemma12BMtpDraftReady(modelId, storageRoot, findInstalledModel);
    }

    public static bool IsDirectLlmStageFallbackReady(
        string? modelId,
        Func<string, InstalledModel?> findInstalledModel)
    {
        if (!Gemma12BLocalValidation.IsTargetModel(modelId))
        {
            return true;
        }

        var installed = findInstalledModel(ReviewModelSelectionResolver.DefaultReviewModelId);
        return installed is not null &&
            installed.Role.Equals("review", StringComparison.OrdinalIgnoreCase) &&
            installed.Verified &&
            File.Exists(installed.FilePath) &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath);
    }

    public static bool IsReviewRuntimeReady(string modelId, string llamaCompletionPath)
    {
        return File.Exists(llamaCompletionPath) &&
            (!RequiresGemma12BMtpAssets(modelId) ||
             File.Exists(Gemma12BLocalValidation.ResolveLlamaServerPath(llamaCompletionPath)));
    }

    public static bool IsGemma12BMtpDraftReady(
        string? modelId,
        string? storageRoot,
        Func<string, InstalledModel?> findInstalledModel)
    {
        if (!RequiresGemma12BMtpAssets(modelId))
        {
            return true;
        }

        var configured = Gemma12BLocalValidation.GetConfiguredMtpDraftModelPath();
        if (configured is not null)
        {
            return LlamaRuntimePathBridge.CanPrepareModelPath(configured);
        }

        var installed = findInstalledModel(Gemma12BLocalValidation.MtpDraftModelId);
        if (installed is not null &&
            installed.Role.Equals("review_aux", StringComparison.OrdinalIgnoreCase) &&
            InstalledPathExists(installed) &&
            LlamaRuntimePathBridge.CanPrepareModelPath(installed.FilePath))
        {
            return true;
        }

        var fallbackPath = string.IsNullOrWhiteSpace(storageRoot)
            ? Gemma12BLocalValidation.ResolveMtpDraftModelPath()
            : Gemma12BLocalValidation.ResolveMtpDraftModelPath(storageRoot);
        return LlamaRuntimePathBridge.CanPrepareModelPath(fallbackPath);
    }

    public static bool ModelPathExists(
        string modelId,
        string fallbackPath,
        Func<string, InstalledModel?> findInstalledModel)
    {
        if (InstalledPathExists(findInstalledModel(modelId)))
        {
            return true;
        }

        return File.Exists(fallbackPath) || Directory.Exists(fallbackPath);
    }

    private static bool IsFasterWhisperRuntimeReady(AppPaths paths)
    {
        return File.Exists(paths.FasterWhisperScriptPath) && FasterWhisperRuntimeLayout.HasPackage(paths);
    }

    private static bool RequiresGemma12BMtpAssets(string? modelId)
    {
        return Gemma12BLocalValidation.IsTargetModel(modelId) &&
            Gemma12BLocalValidation.IsMtpServerEnabled();
    }

    private static bool InstalledPathExists(InstalledModel? installed)
    {
        return installed is not null &&
            installed.Verified &&
            (File.Exists(installed.FilePath) || Directory.Exists(installed.FilePath));
    }
}
