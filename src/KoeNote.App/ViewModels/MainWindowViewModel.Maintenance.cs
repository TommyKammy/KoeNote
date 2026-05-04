using System.IO;
using KoeNote.App.Services;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RunDatabaseMaintenanceAsync()
    {
        if (!CanRunDatabaseMaintenance())
        {
            return;
        }

        IsDatabaseMaintenanceInProgress = true;
        LatestLog = "Running database maintenance...";

        try
        {
            var summary = await Task.Run(() => _databaseMaintenanceService.Run());
            DatabaseMaintenanceSummary = FormatDatabaseMaintenanceSummary(summary);
            LatestLog = DatabaseMaintenanceSummary;
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DatabaseMaintenanceSummary = $"Database maintenance failed: {exception.Message}";
            LatestLog = DatabaseMaintenanceSummary;
        }
        finally
        {
            IsDatabaseMaintenanceInProgress = false;
        }
    }

    private bool CanRunDatabaseMaintenance()
    {
        return !IsDatabaseMaintenanceInProgress && !IsRunInProgress;
    }

    private void RefreshDatabaseMaintenanceSummary()
    {
        DatabaseMaintenanceSummary = $"Database: {FormatBytes(_databaseMaintenanceService.GetDatabaseSize())}";
    }

    private static string FormatDatabaseMaintenanceSummary(DatabaseMaintenanceSummary summary)
    {
        return "Database maintenance completed: " +
            $"logs -{summary.DeletedLogEvents}, " +
            $"undo -{summary.DeletedReviewHistory}, " +
            $"memory events -{summary.DeletedCorrectionMemoryEvents}, " +
            $"size {FormatBytes(summary.SizeBeforeBytes)} -> {FormatBytes(summary.SizeAfterBytes)}, " +
            $"freed {FormatBytes(summary.FreedBytes)}, " +
            $"vacuum {(summary.Vacuumed ? "run" : "skipped")}.";
    }
}
