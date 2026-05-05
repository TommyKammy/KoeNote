using KoeNote.App.Models;

namespace KoeNote.App.Services.Diarization;

public sealed record DiarizationTurn(
    double StartSeconds,
    double EndSeconds,
    string SpeakerId);

public sealed record DiarizationWorkerOutput(
    string Status,
    IReadOnlyList<DiarizationTurn> Turns);

public sealed record DiarizationRunResult(
    string Status,
    string RawOutputPath,
    IReadOnlyList<TranscriptSegment> Segments,
    int SpeakerCount,
    int AssignedSegmentCount,
    TimeSpan Duration);
