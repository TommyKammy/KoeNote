using System.IO;

namespace KoeNote.App.Services.Asr;

public sealed class VibeVoiceCrispAsrEngine(
    AsrWorker worker,
    AsrRunRepository asrRunRepository) : IAsrEngine
{
    public const string Id = "vibevoice-crispasr";

    public string EngineId => Id;

    public string DisplayName => "VibeVoice ASR via CrispASR";

    public Task<AsrEngineCheckResult> CheckAsync(
        AsrEngineConfig config,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        if (!File.Exists(config.RuntimePath))
        {
            messages.Add($"Missing ASR runtime: {config.RuntimePath}");
        }

        if (!File.Exists(config.ModelPath))
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
        try
        {
            var result = await worker.RunAsync(new AsrRunOptions(
                input.JobId,
                input.NormalizedAudioPath,
                config.RuntimePath,
                config.ModelPath,
                config.OutputDirectory,
                options.Hotwords,
                options.Context,
                options.Timeout,
                asrRunId),
                cancellationToken);

            asrRunRepository.MarkSucceeded(
                asrRunId,
                result.Duration,
                result.RawOutputPath,
                result.NormalizedSegmentsPath);

            return new AsrResult(
                asrRunId,
                result.JobId,
                result.RawOutputPath,
                result.NormalizedSegmentsPath,
                result.Segments,
                result.Duration);
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
}
