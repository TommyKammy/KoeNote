namespace KoeNote.App.Services.Review;

public enum ReviewFailureCategory
{
    Unknown,
    MissingRuntime,
    MissingModel,
    MissingSegments,
    ProcessFailed,
    JsonParseFailed,
    NoDrafts
}
