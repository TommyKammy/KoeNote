namespace KoeNote.App.Models;

public sealed record JobSummary(
    string Title,
    string FileName,
    string Status,
    int ProgressPercent,
    int UnreviewedDrafts,
    DateTimeOffset UpdatedAt);
