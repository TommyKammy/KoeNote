using System.IO;

namespace KoeNote.App.Services.Asr;

public sealed class ScriptedJsonAsrEngine(
    string engineId,
    string displayName,
    string sourceName,
    ExternalProcessRunner processRunner,
    AsrJsonNormalizer normalizer,
    AsrResultStore resultStore,
    TranscriptSegmentRepository transcriptSegmentRepository,
    AsrRunRepository asrRunRepository) : IAsrEngine
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
            var processResult = await processRunner.RunAsync(config.RuntimePath, arguments, timeout, cancellationToken);
            if (processResult.ExitCode != 0)
            {
                throw new AsrWorkerException(
                    AsrFailureCategory.ProcessFailed,
                    $"{DisplayName} exited with code {processResult.ExitCode}: {processResult.StandardError}");
            }

            var rawOutput = File.Exists(scriptJsonPath)
                ? File.ReadAllText(scriptJsonPath)
                : AsrOutputExtractor.ExtractJson(processResult.StandardOutput, processResult.StandardError);
            var rawOutputPath = resultStore.SaveRawOutput(config.OutputDirectory, rawOutput);
            var segments = normalizer.Normalize(input.JobId, rawOutput)
                .Select(segment => segment with { Source = sourceName, AsrRunId = asrRunId })
                .ToArray();
            var normalizedSegmentsPath = resultStore.SaveNormalizedSegments(config.OutputDirectory, segments);
            transcriptSegmentRepository.SaveSegments(segments);
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

        return arguments;
    }

    private static bool IsCommandName(string value)
    {
        return !value.Contains(Path.DirectorySeparatorChar)
            && !value.Contains(Path.AltDirectorySeparatorChar)
            && !Path.IsPathRooted(value);
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
}
