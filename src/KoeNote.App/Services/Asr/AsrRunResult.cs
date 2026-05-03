using KoeNote.App.Models;

namespace KoeNote.App.Services.Asr;

public sealed record AsrRunResult(
    string JobId,
    string RawOutputPath,
    string NormalizedSegmentsPath,
    IReadOnlyList<TranscriptSegment> Segments,
    TimeSpan Duration);
