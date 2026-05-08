namespace KoeNote.App.Services.Export;

public sealed record TranscriptExportDialogSelection(
    string FilePath,
    TranscriptExportFormat Format,
    TranscriptExportSource Source);
