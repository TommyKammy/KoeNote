namespace KoeNote.App.Services.Asr;

public enum AsrFailureCategory
{
    Unknown,
    MissingRuntime,
    MissingModel,
    MissingAudio,
    ProcessFailed,
    JsonParseFailed,
    NoSegments
}
