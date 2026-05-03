namespace KoeNote.App.Services.Asr;

public sealed class AsrWorkerException : Exception
{
    public AsrWorkerException(AsrFailureCategory category, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Category = category;
    }

    public AsrFailureCategory Category { get; }
}
