using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace KoeNote.Updater;

public sealed class WpfUpdaterProgressReporter : IUpdaterProgressReporter
{
    private readonly TaskCompletionSource<bool> _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _uiThread;
    private Dispatcher? _dispatcher;
    private UpdaterProgressWindow? _window;
    private Application? _application;
    private bool _isStarted;

    public static IUpdaterProgressReporter CreateIfInteractive()
    {
        return Environment.UserInteractive
            ? new WpfUpdaterProgressReporter()
            : NullUpdaterProgressReporter.Instance;
    }

    public void Show(UpdaterOptions options)
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        _uiThread = new Thread(() => RunWindow(options.Version))
        {
            IsBackground = true,
            Name = "KoeNote updater progress"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _ready.Task.Wait(TimeSpan.FromSeconds(5));
    }

    public void ReportStatus(string title, string message)
    {
        Post(window => window.SetStatus(title, message));
    }

    public async Task ReportTerminalAsync(string title, string message, string logPath, CancellationToken cancellationToken)
    {
        Post(window => window.SetTerminalStatus(title, message, logPath));
        try
        {
            await _closed.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_isStarted)
        {
            return;
        }

        if (_dispatcher is { HasShutdownStarted: false, HasShutdownFinished: false } dispatcher)
        {
            await dispatcher.InvokeAsync(() =>
            {
                _window?.CloseProgrammatically();
                _application?.Shutdown();
            });
        }
    }

    private void RunWindow(string version)
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
            _window = new UpdaterProgressWindow(version);
            _window.Closed += (_, _) => _closed.TrySetResult(true);
            _window.Show();
            _ready.TrySetResult(true);
            _application.Run();
        }
        catch
        {
            _ready.TrySetResult(false);
            _closed.TrySetResult(true);
        }
    }

    private void Post(Action<UpdaterProgressWindow> update)
    {
        if (!_isStarted)
        {
            return;
        }

        _ready.Task.Wait(TimeSpan.FromSeconds(5));
        var dispatcher = _dispatcher;
        var window = _window;
        if (dispatcher is null || window is null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
        {
            return;
        }

        dispatcher.BeginInvoke(new Action(() =>
        {
            if (!window.IsLoaded)
            {
                return;
            }

            update(window);
        }));
    }
}
