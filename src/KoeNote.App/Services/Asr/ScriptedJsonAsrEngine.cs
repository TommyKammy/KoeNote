using System.IO;
using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Services.Asr;

public sealed class ScriptedJsonAsrEngine(
    string engineId,
    string displayName,
    string sourceName,
    ExternalProcessRunner processRunner,
    AsrJsonNormalizer normalizer,
    AsrResultStore resultStore,
    TranscriptSegmentRepository transcriptSegmentRepository,
    AsrRunRepository asrRunRepository,
    JobLogRepository jobLogRepository) : IAsrEngine
{
    public string EngineId => engineId;

    public string DisplayName => displayName;

    public Task<AsrEngineCheckResult> CheckAsync(
        AsrEngineConfig config,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(config.WorkerPath) || !File.Exists(config.WorkerPath))
        {
            messages.Add($"Missing ASR worker script: {config.WorkerPath}");
        }

        if (!IsCommandName(config.RuntimePath) && !File.Exists(config.RuntimePath))
        {
            messages.Add($"Missing ASR runtime: {config.RuntimePath}");
        }

        if (!File.Exists(config.ModelPath) && !Directory.Exists(config.ModelPath))
        {
            messages.Add($"Missing ASR model: {config.ModelPath}");
        }

        return Task.FromResult(new AsrEngineCheckResult(messages.Count == 0, messages));
    }

    public async Task<AsrResult> TranscribeAsync(
        AsrInput input,
        AsrEngineConfig config,
        AsrOptions options,
        CancellationToken cancellationToken = default)
    {
        var asrRunId = asrRunRepository.Start(input.JobId, EngineId, config.ModelId, config.ModelVersion);
        var scriptJsonPath = Path.Combine(config.OutputDirectory, $"{EngineId}.json");
        var timeout = options.Timeout ?? TimeSpan.FromHours(2);

        try
        {
            ValidateInputs(input, config);
            Directory.CreateDirectory(config.OutputDirectory);
            var arguments = BuildArguments(input, config, options, scriptJsonPath);
            var processEnvironment = BuildProcessEnvironment(config);
            var processResult = await processRunner.RunAsync(
                config.RuntimePath,
                arguments,
                timeout,
                cancellationToken,
                processEnvironment.Environment);
            var workerLogPath = SaveAsrWorkerLog(
                input,
                config,
                asrRunId,
                arguments,
                scriptJsonPath,
                processResult,
                processEnvironment);
            if (processResult.ExitCode != 0)
            {
                if (!File.Exists(scriptJsonPath))
                {
                    var workerOutput = string.IsNullOrWhiteSpace(processResult.StandardError)
                        ? processResult.StandardOutput
                        : processResult.StandardError;
                    var category = ClassifyProcessFailure(processResult.ExitCode, workerOutput);
                    var exitSummary = DescribeExitCode(processResult.ExitCode);
                    throw new AsrWorkerException(
                        category,
                        $"{DisplayName} exited with code {processResult.ExitCode} ({exitSummary}): {processResult.StandardError} Worker log: {workerLogPath}",
                        workerLogPath: workerLogPath);
                }
            }

            var rawOutput = File.Exists(scriptJsonPath)
                ? File.ReadAllText(scriptJsonPath, System.Text.Encoding.UTF8)
                : AsrOutputExtractor.ExtractJson(processResult.StandardOutput, processResult.StandardError);
            var rawOutputPath = resultStore.SaveRawOutput(config.OutputDirectory, rawOutput);
            var segments = normalizer.Normalize(input.JobId, rawOutput)
                .Select(segment => segment with { Source = sourceName, AsrRunId = asrRunId })
                .ToArray();
            var normalizedSegmentsPath = resultStore.SaveNormalizedSegments(config.OutputDirectory, segments);
            transcriptSegmentRepository.ReplaceSegments(input.JobId, segments);
            asrRunRepository.MarkSucceeded(asrRunId, processResult.Duration, rawOutputPath, normalizedSegmentsPath);

            return new AsrResult(asrRunId, input.JobId, rawOutputPath, normalizedSegmentsPath, segments, processResult.Duration);
        }
        catch (AsrWorkerException exception)
        {
            asrRunRepository.MarkFailed(asrRunId, exception.Category.ToString());
            throw;
        }
        catch (OperationCanceledException)
        {
            asrRunRepository.MarkCancelled(asrRunId);
            throw;
        }
        catch
        {
            asrRunRepository.MarkFailed(asrRunId, AsrFailureCategory.Unknown.ToString());
            throw;
        }
    }

    private static IReadOnlyList<string> BuildArguments(
        AsrInput input,
        AsrEngineConfig config,
        AsrOptions options,
        string outputJsonPath)
    {
        var arguments = new List<string>
        {
            config.WorkerPath!,
            "--audio",
            input.NormalizedAudioPath,
            "--model",
            config.ModelPath,
            "--output-json",
            outputJsonPath,
            "--language",
            "ja"
        };

        if (!string.IsNullOrWhiteSpace(options.Context))
        {
            arguments.Add("--context");
            arguments.Add(options.Context);
        }

        foreach (var hotword in options.Hotwords ?? [])
        {
            arguments.Add("--hotword");
            arguments.Add(hotword);
        }

        if (config.ModelId.Equals("kotoba-whisper-v2.2-faster", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("--device");
            arguments.Add("auto");
            arguments.Add("--compute-type");
            arguments.Add("float32");
            arguments.Add("--local-files-only");
            arguments.Add("--chunk-length");
            arguments.Add("5");
            arguments.Add("--condition-on-previous-text");
            arguments.Add("false");
        }

        return arguments;
    }

    private static bool IsCommandName(string value)
    {
        return !value.Contains(Path.DirectorySeparatorChar)
            && !value.Contains(Path.AltDirectorySeparatorChar)
            && !Path.IsPathRooted(value);
    }

    private static bool IsCudaRuntimeFailure(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var mentionsCudaRuntimeDll =
            value.Contains("cublas", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cudnn", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cudart", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("zlibwapi", StringComparison.OrdinalIgnoreCase);
        if (!mentionsCudaRuntimeDll)
        {
            return false;
        }

        return value.Contains("could not load", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("failed to load", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("not found", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("cannot open shared object", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("specified module could not be found", StringComparison.OrdinalIgnoreCase);
    }

    private static AsrFailureCategory ClassifyProcessFailure(int exitCode, string workerOutput)
    {
        if (IsCudaRuntimeFailure(workerOutput))
        {
            return AsrFailureCategory.CudaRuntimeMissing;
        }

        return IsNativeCrashExitCode(exitCode)
            ? AsrFailureCategory.NativeCrash
            : AsrFailureCategory.ProcessFailed;
    }

    private string SaveAsrWorkerLog(
        AsrInput input,
        AsrEngineConfig config,
        string asrRunId,
        IReadOnlyList<string> arguments,
        string outputJsonPath,
        ProcessRunResult processResult,
        AsrProcessEnvironment processEnvironment)
    {
        var metadata = new Dictionary<string, string>
        {
            ["engine_id"] = EngineId,
            ["display_name"] = DisplayName,
            ["runtime_path"] = config.RuntimePath,
            ["worker_script_path"] = config.WorkerPath ?? "(unset)",
            ["argument_summary"] = BuildArgumentSummary(arguments),
            ["model_id"] = config.ModelId,
            ["model_version"] = config.ModelVersion ?? "(unset)",
            ["model_path"] = config.ModelPath,
            ["normalized_audio_path"] = input.NormalizedAudioPath,
            ["output_json_path"] = outputJsonPath,
            ["exit_code"] = processResult.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["exit_summary"] = DescribeExitCode(processResult.ExitCode),
            ["duration"] = processResult.Duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            ["asr_path_entries"] = processEnvironment.AddedPathEntries.Count == 0
                ? "(none)"
                : string.Join(";", processEnvironment.AddedPathEntries),
            ["koenote_asr_tools_dir"] = processEnvironment.Environment.TryGetValue("KOENOTE_ASR_TOOLS_DIR", out var asrToolsDir)
                ? asrToolsDir
                : "(unset)"
        };

        return jobLogRepository.SaveWorkerLog(
            input.JobId,
            "asr",
            processResult.StandardOutput,
            processResult.StandardError,
            metadata,
            $"asr-{asrRunId}.log");
    }

    private static string BuildArgumentSummary(IReadOnlyList<string> arguments)
    {
        var sanitized = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            sanitized.Add(arguments[index]);
            if (arguments[index].Equals("--context", StringComparison.OrdinalIgnoreCase) ||
                arguments[index].Equals("--hotword", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < arguments.Count)
                {
                    sanitized.Add("(redacted)");
                    index++;
                }
            }
        }

        return string.Join(" ", sanitized.Select(QuoteArgument));
    }

    private static string QuoteArgument(string value)
    {
        return value.Any(char.IsWhiteSpace)
            ? $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            : value;
    }

    private static string DescribeExitCode(int exitCode)
    {
        return exitCode switch
        {
            0 => "success",
            -1073740791 => "0xC0000409 STATUS_STACK_BUFFER_OVERRUN/native fail-fast",
            -1073741819 => "0xC0000005 access violation/native crash",
            -1073741571 => "0xC00000FD stack overflow/native crash",
            -1073741515 => "0xC0000135 missing native dependency",
            _ when exitCode < 0 => $"0x{unchecked((uint)exitCode):X8} native process failure",
            _ => "process failed"
        };
    }

    private static bool IsNativeCrashExitCode(int exitCode)
    {
        return exitCode < 0;
    }

    private static AsrProcessEnvironment BuildProcessEnvironment(AsrEngineConfig config)
    {
        var pathEntries = new List<string>();
        var appAsrTools = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        if (Directory.Exists(appAsrTools))
        {
            pathEntries.Add(appAsrTools);
        }

        var workerDirectory = Path.GetDirectoryName(config.WorkerPath);
        if (!string.IsNullOrWhiteSpace(workerDirectory))
        {
            var siblingAsrTools = Path.GetFullPath(Path.Combine(workerDirectory, "..", "..", "tools", "asr"));
            if (Directory.Exists(siblingAsrTools))
            {
                pathEntries.Add(siblingAsrTools);
            }
        }

        if (pathEntries.Count == 0)
        {
            return new AsrProcessEnvironment(new Dictionary<string, string>(), []);
        }

        var existingPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var environment = new Dictionary<string, string>();
        var addedPathEntries = pathEntries.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        pathEntries.Add(existingPath);
        environment["PATH"] = string.Join(Path.PathSeparator, pathEntries.Distinct(StringComparer.OrdinalIgnoreCase));
        environment["KOENOTE_ASR_TOOLS_DIR"] = addedPathEntries[0];
        return new AsrProcessEnvironment(environment, addedPathEntries);
    }

    private static void ValidateInputs(AsrInput input, AsrEngineConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WorkerPath) || !File.Exists(config.WorkerPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingRuntime, $"ASR worker script not found: {config.WorkerPath}");
        }

        if (!IsCommandName(config.RuntimePath) && !File.Exists(config.RuntimePath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingRuntime, $"ASR runtime not found: {config.RuntimePath}");
        }

        if (!File.Exists(config.ModelPath) && !Directory.Exists(config.ModelPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingModel, $"ASR model not found: {config.ModelPath}");
        }

        if (!File.Exists(input.NormalizedAudioPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingAudio, $"Normalized audio not found: {input.NormalizedAudioPath}");
        }
    }

    private sealed record AsrProcessEnvironment(
        IReadOnlyDictionary<string, string> Environment,
        IReadOnlyList<string> AddedPathEntries);
}
