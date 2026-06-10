using System.IO;
using System.Text;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Services.Setup;

public interface ISetupRuntimeSmokeService
{
    IReadOnlyList<SetupSmokeCheck> Run(SetupState state, bool runAsrGpuSmoke);
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

    public IReadOnlyList<SetupSmokeCheck> Run(SetupState state, bool runAsrGpuSmoke)
    {
        return
        [
            CheckAsrRuntimeInvocationPrerequisites(state),
            CheckAsrGpuRuntimeProbe(state, runAsrGpuSmoke),
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

    private SetupSmokeCheck CheckAsrGpuRuntimeProbe(SetupState state, bool runAsrGpuSmoke)
    {
        if (!AsrCudaRuntimeLayout.HasPackage(paths))
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                true,
                "ASR CUDA runtime is not installed; GPU profile probe skipped.");
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

        if (!runAsrGpuSmoke)
        {
            var knownProfile = AsrExecutionProfiles.Resolve(_asrSettingsRepository.Load().ExecutionProfileId);
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                knownProfile.IsGpu,
                knownProfile.IsGpu
                    ? $"Known-good GPU ASR profile: {knownProfile.ProfileId}"
                    : "Run final verification to select a working GPU ASR profile.");
        }

        if (!ShouldUseExplicitFasterWhisperProfile(state.SelectedAsrModelId))
        {
            return new SetupSmokeCheck(
                "ASR GPU profile smoke",
                true,
                $"Selected ASR model does not use explicit GPU profile probing: {state.SelectedAsrModelId}");
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
            AsrExecutionProfiles.Resolve(AsrExecutionProfiles.CudaInt8Float16)
        };
        var failures = new List<string>();
        for (var index = 0; index < profiles.Length; index++)
        {
            var profile = profiles[index];
            try
            {
                var attempt = RunAsrGpuSmokeProfile(installed.FilePath, profile, index + 1);
                if (attempt.Result.ExitCode == 0 && File.Exists(attempt.OutputJsonPath))
                {
                    var current = _asrSettingsRepository.Load();
                    _asrSettingsRepository.Save(current with { ExecutionProfileId = profile.ProfileId });
                    return new SetupSmokeCheck(
                        "ASR GPU profile smoke",
                        true,
                        $"GPU ASR smoke passed with profile {profile.ProfileId}.");
                }

                failures.Add(attempt.Result.ExitCode == 0
                    ? $"{profile.ProfileId}: process succeeded but output JSON was not created"
                    : SummarizeProfileFailure(profile, attempt.Result.ExitCode, attempt.Result.StandardOutput, attempt.Result.StandardError));
            }
            catch (Exception exception) when (exception is TimeoutException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                failures.Add($"{profile.ProfileId}: {exception.GetType().Name} - {exception.Message}");
            }
        }

        return new SetupSmokeCheck(
            "ASR GPU profile smoke",
            false,
            $"GPU ASR smoke failed for all CUDA profiles. {string.Join(" | ", failures)}");
    }

    private AsrGpuSmokeAttempt RunAsrGpuSmokeProfile(string modelPath, AsrExecutionProfile profile, int attemptNumber)
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

        var result = _processRunner.RunAsync(
                paths.AsrPythonPath,
                arguments,
                TimeSpan.FromMinutes(3),
                environment: BuildAsrSmokeEnvironment())
            .GetAwaiter()
            .GetResult();
        return new AsrGpuSmokeAttempt(result, outputJsonPath);
    }

    private IReadOnlyDictionary<string, string> BuildAsrSmokeEnvironment()
    {
        var pathEntries = new[]
            {
                paths.AsrCTranslate2RuntimeDirectory,
                paths.AsrRuntimeDirectory,
                Environment.GetEnvironmentVariable("PATH") ?? string.Empty
            }
            .Where(static value => !string.IsNullOrWhiteSpace(value));

        return new Dictionary<string, string>
        {
            ["PATH"] = string.Join(Path.PathSeparator, pathEntries),
            ["KOENOTE_ASR_TOOLS_DIR"] = paths.AsrRuntimeDirectory,
            ["KOENOTE_CTRANSLATE2_CUDA_DIR"] = paths.AsrCTranslate2RuntimeDirectory
        };
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

    private static string SummarizeProfileFailure(
        AsrExecutionProfile profile,
        int exitCode,
        string standardOutput,
        string standardError)
    {
        var output = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        var category = ClassifySmokeFailure(exitCode, output);
        return $"{profile.ProfileId}: {category} (exit {exitCode}) {Trim(output, 500)}";
    }

    private static string ClassifySmokeFailure(int exitCode, string output)
    {
        if (exitCode < 0)
        {
            return "native crash";
        }

        if (MentionsCudaRuntimeDll(output) &&
            (output.Contains("could not load", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("failed to load", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
             output.Contains("specified module could not be found", StringComparison.OrdinalIgnoreCase)))
        {
            return "missing CUDA runtime";
        }

        if (output.Contains("out of memory", StringComparison.OrdinalIgnoreCase))
        {
            return "VRAM or compute-type failure";
        }

        if (output.Contains("compute capability", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("no kernel image", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("unsupported", StringComparison.OrdinalIgnoreCase))
        {
            return "unsupported CUDA or compute type";
        }

        return "process failed";
    }

    private static bool MentionsCudaRuntimeDll(string output)
    {
        return output.Contains("cublas", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("cudnn", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("cudart", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("zlibwapi", StringComparison.OrdinalIgnoreCase);
    }

    private static string Trim(string value, int maxLength)
    {
        var normalized = value.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
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
