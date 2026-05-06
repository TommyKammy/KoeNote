namespace KoeNote.Cleanup;

public sealed record CleanupOptions(
    bool Quiet,
    bool DryRun,
    bool Help,
    bool ListUpdateBackups,
    string? RestoreUpdateBackup,
    bool HasExplicitTargets,
    bool RemoveLogs,
    bool RemoveDownloads,
    bool RemoveUserModels,
    bool RemoveMachineModels,
    bool RemoveUserData)
{
    public static string HelpText => """
        KoeNoteCleanup は KoeNote の任意データを削除する補助ツールです。

        Usage:
          KoeNoteCleanup.exe [--quiet] [--dry-run] [--logs] [--downloads] [--models] [--machine-models] [--user-data]
          KoeNoteCleanup.exe --list-update-backups
          KoeNoteCleanup.exe --restore-update-backup <backup-name-or-path> [--dry-run]

        既定値:
          通常起動では確認ウィンドウを表示します。
          quiet mode は、追加指定がない限りログと一時モデルダウンロードのみ削除します。
          モデル、ジョブ、文字起こし、セットアップ状態、設定は明示的に選択しない限り保持します。
        """;

    public static CleanupOptions Parse(string[] args)
    {
        var set = args
            .Select(static arg => arg.Trim())
            .Where(static arg => arg.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var quiet = set.Contains("--quiet") || set.Contains("/quiet") || set.Contains("/qn");
        var restoreUpdateBackup = ReadOptionValue(args, "--restore-update-backup");
        var explicitTarget = set.Contains("--logs") ||
            set.Contains("--downloads") ||
            set.Contains("--models") ||
            set.Contains("--machine-models") ||
            set.Contains("--user-data");

        return new CleanupOptions(
            Quiet: quiet,
            DryRun: set.Contains("--dry-run"),
            Help: set.Contains("--help") || set.Contains("-h") || set.Contains("/?"),
            ListUpdateBackups: set.Contains("--list-update-backups"),
            RestoreUpdateBackup: restoreUpdateBackup,
            HasExplicitTargets: explicitTarget,
            RemoveLogs: set.Contains("--logs") || (quiet && !explicitTarget),
            RemoveDownloads: set.Contains("--downloads") || (quiet && !explicitTarget),
            RemoveUserModels: set.Contains("--models"),
            RemoveMachineModels: set.Contains("--machine-models"),
            RemoveUserData: set.Contains("--user-data"));
    }

    private static string? ReadOptionValue(string[] args, string optionName)
    {
        for (var index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var valueIndex = index + 1;
            if (valueIndex >= args.Length || args[valueIndex].StartsWith("-", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{optionName} requires a value.");
            }

            return args[valueIndex];
        }

        return null;
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
