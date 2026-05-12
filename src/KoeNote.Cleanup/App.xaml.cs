using System.Windows;

namespace KoeNote.Cleanup;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (ArpMetadataWriter.TryHandle(e.Args))
        {
            Shutdown(0);
            return;
        }

        var options = CleanupOptions.Parse(e.Args);
        var paths = CleanupPaths.FromEnvironment();
        var service = new CleanupService(paths);
        var updateBackupRestoreService = new UpdateBackupRestoreService(paths);

        if (options.Help)
        {
            Console.WriteLine(CleanupOptions.HelpText);
            Shutdown(0);
            return;
        }

        if (options.ListUpdateBackups)
        {
            foreach (var backup in updateBackupRestoreService.ListBackups())
            {
                Console.WriteLine($"{backup.Name}`t{backup.LastWriteTime:O}`t{backup.Path}");
            }

            Shutdown(0);
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.RestoreUpdateBackup))
        {
            var result = updateBackupRestoreService.Restore(options.RestoreUpdateBackup, options.DryRun);
            Console.WriteLine(result.ToConsoleText());
            Shutdown(result.Succeeded ? 0 : 1);
            return;
        }

        if (options.Quiet)
        {
            var result = service.Execute(options.ToPlan(), options.DryRun);
            Console.WriteLine(result.ToConsoleText());
            Shutdown(result.Succeeded ? 0 : 1);
            return;
        }

        var initialPlan = options.HasExplicitTargets
            ? options.ToPlan()
            : CleanupPlan.AppOnly;
        var window = new CleanupWindow(service, initialPlan, options.DryRun);
        MainWindow = window;
        window.Show();
    }
}
