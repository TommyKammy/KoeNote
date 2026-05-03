namespace KoeNote.App.Services.Asr;

public sealed record AsrRunOptions(
    string JobId,
    string NormalizedAudioPath,
    string CrispAsrPath,
    string ModelPath,
    string OutputDirectory,
    IReadOnlyList<string>? Hotwords = null,
    string? Context = null,
    TimeSpan? Timeout = null);
