using System.IO;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

internal interface ISetupRuntimeSmokeService
{
    IReadOnlyList<SetupSmokeCheck> Run(SetupState state);
}

internal sealed class SetupRuntimeSmokeService(
    AppPaths paths,
    InstalledModelRepository installedModelRepository)
    : ISetupRuntimeSmokeService
{
    public IReadOnlyList<SetupSmokeCheck> Run(SetupState state)
    {
        return
        [
            CheckAsrRuntimeInvocationPrerequisites(state),
            CheckReviewRuntimePathBridge(state),
            CheckDiarizationRuntimeData()
        ];
    }

    private SetupSmokeCheck CheckAsrRuntimeInvocationPrerequisites(SetupState state)
    {
        if (string.IsNullOrWhiteSpace(state.SelectedAsrModelId))
        {
            return new SetupSmokeCheck("ASR runtime smoke", false, "Select an ASR model in Setup.");
        }

        var installed = installedModelRepository.FindInstalledModel(state.SelectedAsrModelId);
        if (installed is null || (!File.Exists(installed.FilePath) && !Directory.Exists(installed.FilePath)))
        {
            return new SetupSmokeCheck("ASR runtime smoke", false, $"ASR model is not installed: {state.SelectedAsrModelId}");
        }

        if (!File.Exists(paths.FasterWhisperScriptPath))
        {
            return new SetupSmokeCheck("ASR runtime smoke", false, $"ASR worker script is missing: {paths.FasterWhisperScriptPath}");
        }

        if (!File.Exists(paths.AsrPythonPath))
        {
            return new SetupSmokeCheck("ASR runtime smoke", false, $"ASR Python runtime is missing: {paths.AsrPythonPath}");
        }

        return new SetupSmokeCheck("ASR runtime smoke", true, $"Ready: {paths.FasterWhisperScriptPath}");
    }

    private SetupSmokeCheck CheckReviewRuntimePathBridge(SetupState state)
    {
        if (string.IsNullOrWhiteSpace(state.SelectedReviewModelId))
        {
            return new SetupSmokeCheck("Review runtime smoke", false, "Select a Review model in Setup.");
        }

        var installed = installedModelRepository.FindInstalledModel(state.SelectedReviewModelId);
        if (installed is null || !File.Exists(installed.FilePath))
        {
            return new SetupSmokeCheck("Review runtime smoke", false, $"Review model is not installed: {state.SelectedReviewModelId}");
        }

        if (!File.Exists(paths.LlamaCompletionPath) && !File.Exists(paths.TernaryLlamaCompletionPath))
        {
            return new SetupSmokeCheck("Review runtime smoke", false, $"Review runtime is missing: {paths.LlamaCompletionPath}");
        }

        try
        {
            var smokeRoot = Path.Combine(paths.Root, "setup-smoke");
            Directory.CreateDirectory(smokeRoot);
            var promptPath = Path.Combine(smokeRoot, "review-smoke.prompt.txt");
            var schemaPath = Path.Combine(smokeRoot, "review-smoke.schema.json");
            File.WriteAllText(promptPath, "Return [] only.");
            File.WriteAllText(schemaPath, """{"type":"array"}""");

            using var bridge = LlamaRuntimePathBridge.Create(installed.FilePath);
            var safePromptPath = bridge.AddInputFile(promptPath);
            var safeSchemaPath = bridge.AddInputFile(schemaPath);
            var allSafe = IsAscii(bridge.ModelPath) && IsAscii(safePromptPath) && IsAscii(safeSchemaPath);
            return new SetupSmokeCheck(
                "Review runtime smoke",
                allSafe,
                allSafe ? "ASCII-safe runtime bridge is ready." : "Review runtime bridge produced a non-ASCII path.");
        }
        catch (Exception exception) when (LlamaRuntimePathBridge.IsBridgePreparationException(exception)
            || exception is DirectoryNotFoundException or NotSupportedException)
        {
            return new SetupSmokeCheck(
                "Review runtime smoke",
                false,
                $"Could not prepare ASCII-safe Review runtime paths: {exception.Message}");
        }
    }

    private SetupSmokeCheck CheckDiarizationRuntimeData()
    {
        var ok = DiarizationRuntimeLayout.HasPackage(paths);
        if (ok)
        {
            return new SetupSmokeCheck("speaker diarization smoke", true, "Required runtime data is present.");
        }

        var missing = DiarizationRuntimeLayout.HasManagedPackageMetadata(paths)
            ? DiarizationRuntimeLayout.GetMissingManagedRuntimeData(paths)
            : DiarizationRuntimeLayout.HasLegacyPackageMetadata(paths)
                ? DiarizationRuntimeLayout.GetMissingLegacyRuntimeData(paths)
                : [];
        return new SetupSmokeCheck(
            "speaker diarization smoke",
            false,
            missing.Count == 0
                ? "Speaker diarization runtime is not installed."
                : $"Required runtime data is missing: {string.Join("; ", missing)}");
    }

    private static bool IsAscii(string value)
    {
        return value.All(static character => character <= 0x7f);
    }
}
