namespace KoeNote.Updater;

public interface IUpdaterProgressReporter : IAsyncDisposable
{
    void Show(UpdaterOptions options);

    void ReportStatus(string title, string message);

    Task ReportTerminalAsync(string title, string message, string logPath, CancellationToken cancellationToken);
}

public sealed class NullUpdaterProgressReporter : IUpdaterProgressReporter
{
    public static NullUpdaterProgressReporter Instance { get; } = new();

    private NullUpdaterProgressReporter()
    {
    }

    public void Show(UpdaterOptions options)
    {
    }

    public void ReportStatus(string title, string message)
    {
    }

    public Task ReportTerminalAsync(string title, string message, string logPath, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
