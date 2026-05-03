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
        var arguments = commandBuilder.BuildArguments(options);
        var processResult = await processRunner.RunAsync(options.CrispAsrPath, arguments, timeout, cancellationToken);

        if (processResult.ExitCode != 0)
        {
            throw new AsrWorkerException(
                AsrFailureCategory.ProcessFailed,
                $"ASR runtime exited with code {processResult.ExitCode}: {processResult.StandardError}");
        }

        var rawOutput = processResult.StandardOutput;
        var segments = normalizer.Normalize(options.JobId, rawOutput);
        var paths = resultStore.Save(options.OutputDirectory, rawOutput, segments);
        repository.SaveSegments(segments);

        return new AsrRunResult(
            options.JobId,
            paths.RawOutputPath,
            paths.NormalizedSegmentsPath,
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
