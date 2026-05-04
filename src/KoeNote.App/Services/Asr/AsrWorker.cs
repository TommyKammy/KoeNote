using System.IO;

namespace KoeNote.App.Services.Asr;

public sealed class AsrWorker(
    ExternalProcessRunner processRunner,
    AsrCommandBuilder commandBuilder,
    AsrJsonNormalizer normalizer,
    AsrResultStore resultStore,
    TranscriptSegmentRepository repository)
{
    public async Task<AsrRunResult> RunAsync(AsrRunOptions options, CancellationToken cancellationToken = default)
    {
        ValidateInputs(options);

        var timeout = options.Timeout ?? TimeSpan.FromHours(2);
        Directory.CreateDirectory(options.OutputDirectory);
        var arguments = commandBuilder.BuildArgumentList(options);
        var processResult = await processRunner.RunAsync(options.CrispAsrPath, arguments, timeout, cancellationToken);

        if (processResult.ExitCode != 0)
        {
            throw new AsrWorkerException(
                AsrFailureCategory.ProcessFailed,
                $"ASR runtime exited with code {processResult.ExitCode}: {processResult.StandardError}");
        }

        var runtimeJsonPath = AsrCommandBuilder.GetJsonOutputPath(options);
        var rawOutput = File.Exists(runtimeJsonPath)
            ? File.ReadAllText(runtimeJsonPath)
            : AsrOutputExtractor.ExtractJson(processResult.StandardOutput, processResult.StandardError);
        var rawOutputPath = resultStore.SaveRawOutput(options.OutputDirectory, rawOutput);
        var segments = normalizer.Normalize(options.JobId, rawOutput);
        if (!string.IsNullOrWhiteSpace(options.AsrRunId))
        {
            segments = segments
                .Select(segment => segment with { AsrRunId = options.AsrRunId })
                .ToArray();
        }

        var normalizedSegmentsPath = resultStore.SaveNormalizedSegments(options.OutputDirectory, segments);
        repository.SaveSegments(segments);

        return new AsrRunResult(
            options.JobId,
            rawOutputPath,
            normalizedSegmentsPath,
            segments,
            processResult.Duration);
    }

    private static void ValidateInputs(AsrRunOptions options)
    {
        if (!File.Exists(options.CrispAsrPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingRuntime, $"ASR runtime not found: {options.CrispAsrPath}");
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingModel, $"ASR model not found: {options.ModelPath}");
        }

        if (!File.Exists(options.NormalizedAudioPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingAudio, $"Normalized audio not found: {options.NormalizedAudioPath}");
        }
    }
}
