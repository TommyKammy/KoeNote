using KoeNote.App.Models;

namespace KoeNote.App.Services.Review;

public sealed record ReviewRunResult(
    string JobId,
    string RawOutputPath,
    string NormalizedDraftsPath,
    IReadOnlyList<CorrectionDraft> Drafts,
    TimeSpan Duration);
