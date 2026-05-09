namespace KoeNote.App.Models;

public enum PostProcessMode
{
    ReviewOnly,
    SummaryOnly,

    // Legacy compatibility path. Normal UX exposes review and summary as separate manual actions.
    ReviewAndSummary
}
