using System.Windows;
using System.Windows.Threading;
using KoeNote.App.Services;
using KoeNote.App.Services.Diagnostics;

namespace KoeNote.App;

public partial class App : Application
{
    private CrashLogService? _crashLogService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var paths = new AppPaths();
        paths.EnsureCreated();
        _crashLogService = new CrashLogService(paths);
        _crashLogService.WriteAppStartLog();

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteCrashLog("DispatcherUnhandledException", e.Exception);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            WriteCrashLog("AppDomainUnhandledException", exception);
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteCrashLog("UnobservedTaskException", e.Exception);
    }

    private void WriteCrashLog(string source, Exception exception)
    {
        try
        {
            _crashLogService?.WriteExceptionLog(source, exception);
        }
        catch
        {
            // Crash logging must never mask the original exception.
        }
    }
}
