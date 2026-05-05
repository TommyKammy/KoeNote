namespace KoeNote.Cleanup;

public sealed record CleanupOptions(
    bool Quiet,
    bool DryRun,
    bool Help,
    bool HasExplicitTargets,
    bool RemoveLogs,
    bool RemoveDownloads,
    bool RemoveUserModels,
    bool RemoveMachineModels,
    bool RemoveUserData)
{
    public static string HelpText => """
        KoeNoteCleanup removes optional KoeNote data after MSI uninstall.

        Usage:
          KoeNoteCleanup.exe [--quiet] [--dry-run] [--logs] [--downloads] [--models] [--machine-models] [--user-data]

        Defaults:
          Interactive mode opens a confirmation window.
          Quiet mode removes logs and temporary model downloads only unless more switches are provided.
          Models, jobs, transcripts, setup state, and settings are kept unless explicitly selected.
        """;

    public static CleanupOptions Parse(string[] args)
    {
        var set = args
            .Select(static arg => arg.Trim())
            .Where(static arg => arg.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var quiet = set.Contains("--quiet") || set.Contains("/quiet") || set.Contains("/qn");
        var explicitTarget = set.Contains("--logs") ||
            set.Contains("--downloads") ||
            set.Contains("--models") ||
            set.Contains("--machine-models") ||
            set.Contains("--user-data");

        return new CleanupOptions(
            Quiet: quiet,
            DryRun: set.Contains("--dry-run"),
            Help: set.Contains("--help") || set.Contains("-h") || set.Contains("/?"),
            HasExplicitTargets: explicitTarget,
            RemoveLogs: set.Contains("--logs") || (quiet && !explicitTarget),
            RemoveDownloads: set.Contains("--downloads") || (quiet && !explicitTarget),
            RemoveUserModels: set.Contains("--models"),
            RemoveMachineModels: set.Contains("--machine-models"),
            RemoveUserData: set.Contains("--user-data"));
    }

    public CleanupPlan ToPlan()
    {
        return new CleanupPlan(
            RemoveLogs,
            RemoveDownloads,
            RemoveUserModels,
            RemoveMachineModels,
            RemoveUserData);
    }
}
