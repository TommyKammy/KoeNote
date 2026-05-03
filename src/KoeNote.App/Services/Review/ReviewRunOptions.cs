using KoeNote.App.Models;

namespace KoeNote.App.Services.Review;

public sealed record ReviewRunOptions(
    string JobId,
    string LlamaCompletionPath,
    string ModelPath,
    string OutputDirectory,
    IReadOnlyList<TranscriptSegment> Segments,
    double MinConfidence = 0.5,
    TimeSpan? Timeout = null);
