namespace KoeNote.App.Services.Audio;

public sealed record AudioPreprocessResult(
    string NormalizedAudioPath,
    string LogPath,
    TimeSpan Duration,
    int ExitCode);
