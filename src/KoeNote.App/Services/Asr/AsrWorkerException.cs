namespace KoeNote.App.Services.Asr;

public sealed class AsrWorkerException : Exception
{
    public AsrWorkerException(
        AsrFailureCategory category,
        string message,
        Exception? innerException = null,
        string? workerLogPath = null)
        : base(message, innerException)
    {
        Category = category;
        WorkerLogPath = workerLogPath;
    }

    public AsrFailureCategory Category { get; }

    public string? WorkerLogPath { get; }
}
