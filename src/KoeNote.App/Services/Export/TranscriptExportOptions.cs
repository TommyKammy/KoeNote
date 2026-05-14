namespace KoeNote.App.Services.Export;

public sealed record TranscriptExportOptions(
    string? BaseFileName = null,
    bool IncludeTimestamps = true,
    TranscriptExportSource Source = TranscriptExportSource.ReadablePolished,
    bool MergeConsecutiveSpeakers = false);
