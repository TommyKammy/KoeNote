using System.ComponentModel;
using System.IO;
using System.Text;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

public interface ISetupRuntimeSmokeService
{
    IReadOnlyList<SetupSmokeCheck> RunReadiness(SetupState state, bool nvidiaGpuDetected);

    Task<IReadOnlyList<SetupSmokeCheck>> RunAsync(
        SetupState state,
        bool runAsrGpuSmoke,
        bool nvidiaGpuDetected,
        CancellationToken cancellationToken = default);
}

public sealed class SetupRuntimeSmokeService(
    AppPaths paths,
    InstalledModelRepository installedModelRepository,
    ExternalProcessRunner? processRunner = null,
    AsrSettingsRepository? asrSettingsRepository = null)
    : ISetupRuntimeSmokeService
{
    private readonly ExternalProcessRunner _processRunner = processRunner ?? new ExternalProcessRunner();
    private readonly AsrSettingsRepository _asrSettingsRepository = asrSettingsRepository ?? new AsrSettingsRepository(paths);

    public IReadOnlyList<SetupSmokeCheck> RunReadiness(SetupState state, bool nvidiaGpuDetected)
    {
        var asrRuntimeSmoke = CheckAsrRuntimeInvocationPrerequisites(state);
        return
        [
            asrRuntimeSmoke,
            asrRuntimeSmoke.IsOk
                ? CheckAsrGpuRuntimeReadiness(state, nvidiaGpuDetected)
                : SetupRuntimeSmokeResultProjector.CreateSkippedAsrGpuSmokeCheck(asrRuntimeSmoke),
            CheckReviewRuntimePathBridge(state),
            CheckDiarizationRuntimeData()
        ];
    }

    public async Task<IReadOnlyList<SetupSmokeCheck>> RunAsync(
        SetupState state,
        bool runAsrGpuSmoke,
        bool nvidiaGpuDetected,
        CancellationToken cancellationToken = default)
    {
        if (!runAsrGpuSmoke)
        {
            return RunReadiness(state, nvidiaGpuDetected);
        }

        var asrRuntimeSmoke = CheckAsrRuntimeInvocationPrerequisites(state);
        return
        [
            asrRuntimeSmoke,
            asrRuntimeSmoke.IsOk
                ? await CheckAsrGpuRuntimeProbeAsync(state, nvidiaGpuDetected, cancellationToken)
                : SetupRuntimeSmokeResultProjector.CreateSkippedAsrGpuSmokeCheck(asrRuntimeSmoke),
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

    private async Task<SetupSmokeCheck> CheckAsrGpuRuntimeProbeAsync(
        SetupState state,
        bool nvidiaGpuDetected,
        CancellationToken cancellationToken)
    {
        var readiness = CheckAsrGpuRuntimeProbePreconditions(state, nvidiaGpuDetected);
        if (readiness is not null)
        {
            return readiness;
        }

        var installed = string.IsNullOrWhiteSpace(state.SelectedAsrModelId)
            ? null
            : installedModelRepository.FindInstalledModel(state.SelectedAsrModelId);
        if (installed is null || (!File.Exists(installed.FilePath) && !Directory.Exists(installed.FilePath)))
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                false,
                $"ASR model is not installed: {state.SelectedAsrModelId}");
        }

        var profiles = new[]
        {
            AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaFloat16),
            AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaInt8Float16),
            AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaFloat32)
        };
        var failures = new List<string>();
        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            try
            {
                var attempt = await RunAsrGpuSmokeProfileAsync(installed.FilePath, profile, index + 1, cancellationToken);
                var outputJsonCreated = File.Exists(attempt.OutputJsonPath);
                if (attempt.Result.ExitCode == 0 && outputJsonCreated)
                {
                    var current = _asrSettingsRepository.Load();
                    _asrSettingsRepository.Save(current with { ExecutionProfileId = profile.ProfileId });
                    return SetupRuntimeSmokeResultProjector.CreatePassedAsrGpuSmokeCheck(profile);
                }

                failures.Add(SetupRuntimeSmokeResultProjector.SummarizeProfileAttempt(
                    profile,
                    attempt.Result,
                    outputJsonCreated));
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception)
            {
                failures.Add(SetupRuntimeSmokeResultProjector.SummarizeProfileException(profile, exception));
            }
        }

        return SetupRuntimeSmokeResultProjector.CreateFailedAsrGpuSmokeCheck(failures);
    }

    private SetupSmokeCheck CheckAsrGpuRuntimeReadiness(SetupState state, bool nvidiaGpuDetected)
    {
        var readiness = CheckAsrGpuRuntimeProbePreconditions(state, nvidiaGpuDetected);
        if (readiness is not null)
        {
            return readiness;
        }

        var knownProfile = AsrExecutionProfiles.Resolve(_asrSettingsRepository.Load().ExecutionProfileId);
        return new SetupSmokeCheck(
            "ASR GPU profile smoke",
            knownProfile.IsGpu,
            knownProfile.IsGpu
                ? $"Known-good GPU ASR profile: {knownProfile.ProfileId}"
                : "Run final verification to select a working GPU ASR profile.");
    }

    private SetupSmokeCheck? CheckAsrGpuRuntimeProbePreconditions(SetupState state, bool nvidiaGpuDetected)
    {
        if (!nvidiaGpuDetected)
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                true,
                "NVIDIA GPU is not detected; GPU ASR profile probe skipped.");
        }

        if (!AsrCudaRuntimeLayout.HasPackage(paths))
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                false,
                $"ASR CUDA runtime is not installed; reinstall ASR GPU runtime before running GPU ASR. Missing: {string.Join("; ", AsrCudaRuntimeLayout.GetMissingPackageItems(paths))}");
        }

        if (!ShouldUseExplicitFasterWhisperProfile(state.SelectedAsrModelId))
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                true,
                $"Selected ASR model does not use explicit GPU profile probing: {state.SelectedAsrModelId}");
        }

        var preferred = AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(paths.AsrCTranslate2RuntimeDirectory)
            ? paths.AsrCTranslate2RuntimeDirectory
            : paths.AsrRuntimeDirectory;
        if (!AsrCudaRuntimeLayout.HasNvidiaRuntimeFiles(preferred))
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                false,
                $"CTranslate2 CUDA DLLs are missing. Reinstall ASR CUDA runtime: {preferred}");
        }

        return null;
    }

    private async Task<AsrGpuSmokeAttempt> RunAsrGpuSmokeProfileAsync(
        string modelPath,
        AsrExecutionProfile profile,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        var smokeRoot = Path.Combine(paths.Root, "setup-smoke", "asr-gpu");
        Directory.CreateDirectory(smokeRoot);
        var audioPath = Path.Combine(smokeRoot, "gpu-smoke.wav");
        var outputJsonPath = Path.Combine(smokeRoot, $"{profile.ProfileId}.json");
        WriteSilentWav(audioPath);
        if (File.Exists(outputJsonPath))
        {
            File.Delete(outputJsonPath);
        }

        var arguments = new List<string>
        {
            paths.FasterWhisperScriptPath,
            "--audio",
            audioPath,
            "--model",
            modelPath,
            "--output-json",
            outputJsonPath,
            "--language",
            "ja",
            "--device",
            profile.Device,
            "--compute-type",
            profile.ComputeType,
            "--execution-profile",
            profile.ProfileId,
            "--attempt-number",
            attemptNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--condition-on-previous-text",
            "false",
            "--diarization",
            "off",
            "--local-files-only"
        };

        var result = await _processRunner.RunAsync(
                paths.AsrPythonPath,
                arguments,
                TimeSpan.FromMinutes(3),
                cancellationToken,
                BuildAsrSmokeEnvironment());
        return new AsrGpuSmokeAttempt(result, outputJsonPath);
    }

    private IReadOnlyDictionary<string, string> BuildAsrSmokeEnvironment()
    {
        var environment = new Dictionary<string, string>();
        if (Directory.Exists(paths.AsrCTranslate2RuntimeDirectory))
        {
            var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            var pathEntries = new[]
                {
                    paths.AsrCTranslate2RuntimeDirectory,
                    existingPath
                }
                .Where(static value => !string.IsNullOrWhiteSpace(value));
            environment["PATH"] = string.Join(Path.PathSeparator, pathEntries);
            environment["KOENOTE_CTRANSLATE2_CUDA_DIR"] = paths.AsrCTranslate2RuntimeDirectory;
        }

        if (Directory.Exists(paths.AsrRuntimeDirectory))
        {
            environment["KOENOTE_ASR_TOOLS_DIR"] = paths.AsrRuntimeDirectory;
        }

        return environment;
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

    private static void WriteSilentWav(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const short channels = 1;
        const int sampleRate = 16000;
        const short bitsPerSample = 16;
        const int seconds = 1;
        var byteRate = sampleRate * channels * bitsPerSample / 8;
        short blockAlign = channels * bitsPerSample / 8;
        var dataSize = sampleRate * seconds * blockAlign;

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]);
    }

    private static bool ShouldUseExplicitFasterWhisperProfile(string? modelId)
    {
        return modelId?.Equals("faster-whisper-large-v3-turbo", StringComparison.OrdinalIgnoreCase) == true ||
            modelId?.Equals("faster-whisper-large-v3", StringComparison.OrdinalIgnoreCase) == true ||
            modelId?.Equals("whisper-base", StringComparison.OrdinalIgnoreCase) == true ||
            modelId?.Equals("whisper-small", StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed record AsrGpuSmokeAttempt(ProcessRunResult Result, string OutputJsonPath);

    private static bool IsAscii(string value)
    {
        return value.All(static character => character <= 0x7f);
    }
}
