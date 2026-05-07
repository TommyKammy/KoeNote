using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Diarization;

public sealed class ScriptedDiarizationService(
    AppPaths paths,
    ExternalProcessRunner processRunner,
    DiarizationJsonNormalizer normalizer,
    DiarizationSegmentAssigner assigner,
    TranscriptSegmentRepository transcriptSegmentRepository,
    AsrResultStore asrResultStore,
    PythonRuntimeResolver? pythonRuntimeResolver = null)
{
    private readonly PythonRuntimeResolver _pythonRuntimeResolver = pythonRuntimeResolver ?? new PythonRuntimeResolver(paths, processRunner);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public async Task<DiarizationRunResult> RunAsync(
        string jobId,
        string normalizedAudioPath,
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var outputDirectory = Path.Combine(paths.Jobs, jobId, "diarization");
        var rawOutputPath = Path.Combine(outputDirectory, "diarize.raw.json");
        Directory.CreateDirectory(outputDirectory);
        var timeout = DiarizationTimeoutPolicy.Estimate(normalizedAudioPath);
        WriteStatus(rawOutputPath, $"running: speaker diarization started; timeout {timeout:g}");

        if (!File.Exists(paths.DiarizeWorkerScriptPath))
        {
            WriteStatus(rawOutputPath, "skipped: diarize worker script not found");
            return new DiarizationRunResult("skipped: diarize worker script not found", rawOutputPath, segments, 0, 0, DateTimeOffset.UtcNow - startedAt);
        }

        ProcessRunResult processResult;
        try
        {
            var runtime = await _pythonRuntimeResolver.ResolveInstalledRuntimeAsync(cancellationToken);
            if (!runtime.IsFound || runtime.Command is null)
            {
                WriteStatus(rawOutputPath, $"failed: {runtime.Message}");
                return new DiarizationRunResult($"failed: {runtime.Message}", rawOutputPath, segments, 0, 0, DateTimeOffset.UtcNow - startedAt);
            }

            processResult = await processRunner.RunAsync(
                runtime.Command.FileName,
                runtime.Command.BuildArguments(
                    paths.DiarizeWorkerScriptPath,
                    "--audio",
                    normalizedAudioPath,
                    "--output-json",
                    rawOutputPath),
                timeout,
                cancellationToken,
                runtime.Command.Environment);
        }
        catch (TimeoutException exception)
        {
            WriteStatus(rawOutputPath, $"failed: {exception.Message}");
            return new DiarizationRunResult($"failed: {exception.Message}", rawOutputPath, segments, 0, 0, DateTimeOffset.UtcNow - startedAt);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WriteStatus(rawOutputPath, $"failed: {exception.Message}");
            return new DiarizationRunResult($"failed: {exception.Message}", rawOutputPath, segments, 0, 0, DateTimeOffset.UtcNow - startedAt);
        }

        if (!File.Exists(rawOutputPath))
        {
            WriteStatus(rawOutputPath, processResult.ExitCode == 0
                ? "skipped: diarize produced no output"
                : $"failed: diarize exited with code {processResult.ExitCode}: {processResult.StandardError}");
        }

        var rawJson = File.ReadAllText(rawOutputPath, Encoding.UTF8);
        DiarizationWorkerOutput output;
        try
        {
            output = normalizer.Normalize(rawJson);
        }
        catch (JsonException exception)
        {
            WriteStatus(rawOutputPath, $"failed: invalid diarize JSON: {exception.Message}");
            return new DiarizationRunResult($"failed: invalid diarize JSON: {exception.Message}", rawOutputPath, segments, 0, 0, processResult.Duration);
        }

        if (output.Turns.Count == 0)
        {
            return new DiarizationRunResult(output.Status, rawOutputPath, segments, 0, 0, processResult.Duration);
        }

        var updatedSegments = assigner.Assign(segments, output.Turns);
        transcriptSegmentRepository.SaveSegments(updatedSegments);
        asrResultStore.SaveNormalizedSegments(Path.Combine(paths.Jobs, jobId, "asr"), updatedSegments);

        var assignedSegmentCount = CountAssignedSegments(updatedSegments, output.Turns);
        var speakerCount = output.Turns
            .Select(turn => turn.SpeakerId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        if (assignedSegmentCount == 0)
        {
            return new DiarizationRunResult("skipped: diarize turns did not overlap ASR segments", rawOutputPath, updatedSegments, speakerCount, 0, processResult.Duration);
        }

        speakerCount = updatedSegments
            .Select(segment => segment.SpeakerId)
            .Where(speaker => !string.IsNullOrWhiteSpace(speaker))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        return new DiarizationRunResult(output.Status, rawOutputPath, updatedSegments, speakerCount, assignedSegmentCount, processResult.Duration);
    }

    private static int CountAssignedSegments(
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<DiarizationTurn> turns)
    {
        return segments.Count(segment =>
        {
            if (string.IsNullOrWhiteSpace(segment.SpeakerId))
            {
                return false;
            }

            return turns.Any(turn =>
                string.Equals(turn.SpeakerId, segment.SpeakerId, StringComparison.OrdinalIgnoreCase) &&
                Math.Min(segment.EndSeconds, turn.EndSeconds) > Math.Max(segment.StartSeconds, turn.StartSeconds));
        });
    }

    private static void WriteStatus(string path, string status)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            engine = "diarize",
            status,
            turns = Array.Empty<object>()
        }, JsonOptions), Encoding.UTF8);
    }
}
