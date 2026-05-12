namespace KoeNote.App.Services.Asr;

public enum AsrFailureCategory
{
    Unknown,
    MissingRuntime,
    MissingModel,
    MissingAudio,
    CudaRuntimeMissing,
    NativeCrash,
    ProcessFailed,
    JsonParseFailed,
    NoSegments
}
