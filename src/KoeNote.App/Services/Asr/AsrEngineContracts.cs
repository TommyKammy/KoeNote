using KoeNote.App.Models;

namespace KoeNote.App.Services.Asr;

public interface IAsrEngine
{
    string EngineId { get; }

    string DisplayName { get; }

    Task<AsrEngineCheckResult> CheckAsync(
        AsrEngineConfig config,
        CancellationToken cancellationToken = default);

    Task<AsrResult> TranscribeAsync(
        AsrInput input,
        AsrEngineConfig config,
        AsrOptions options,
        CancellationToken cancellationToken = default);
}

public sealed record AsrEngineConfig(
    string RuntimePath,
    string ModelPath,
    string OutputDirectory,
    string ModelId,
    string? WorkerPath = null,
    string? ModelVersion = null);

public sealed record AsrInput(
    string JobId,
    string NormalizedAudioPath);

public sealed record AsrOptions(
    IReadOnlyList<string>? Hotwords = null,
    string? Context = null,
    TimeSpan? Timeout = null);

public sealed record AsrEngineCheckResult(
    bool IsAvailable,
    IReadOnlyList<string> Messages);

public sealed record AsrResult(
    string AsrRunId,
    string JobId,
    string RawOutputPath,
    string NormalizedSegmentsPath,
    IReadOnlyList<TranscriptSegment> Segments,
    TimeSpan Duration);
