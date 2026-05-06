using System.IO;

namespace KoeNote.Cleanup;

public sealed record UpdateBackupInfo(string Name, string Path, DateTimeOffset LastWriteTime);

public sealed record UpdateBackupRestoreAction(string Path, bool Changed, string Message);

public sealed record UpdateBackupRestoreResult(IReadOnlyList<UpdateBackupRestoreAction> Actions)
{
    public bool Succeeded => Actions.All(static action => action.Changed || !action.Message.StartsWith("Failed:", StringComparison.Ordinal));

    public string ToConsoleText()
    {
        return string.Join(Environment.NewLine, Actions.Select(static action => $"{(action.Changed ? "Changed" : "Kept")}: {action.Path} ({action.Message})"));
    }
}

public sealed class UpdateBackupRestoreService(CleanupPaths paths)
{
    public IReadOnlyList<UpdateBackupInfo> ListBackups()
    {
        if (!Directory.Exists(paths.UpdateBackups))
        {
            return [];
        }

        return Directory.EnumerateDirectories(paths.UpdateBackups)
            .Select(static path => new DirectoryInfo(path))
            .Where(static directory => !directory.Name.StartsWith("pre-restore-", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(static directory => directory.LastWriteTimeUtc)
            .Select(static directory => new UpdateBackupInfo(
                directory.Name,
                directory.FullName,
                new DateTimeOffset(directory.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToArray();
    }

    public UpdateBackupRestoreResult Restore(string backupNameOrPath, bool dryRun)
    {
        var backupDirectory = ResolveBackupDirectory(backupNameOrPath);
        var actions = new List<UpdateBackupRestoreAction>();

        if (!Directory.Exists(backupDirectory))
        {
            return new UpdateBackupRestoreResult([
                new UpdateBackupRestoreAction(backupDirectory, false, "Failed: backup directory does not exist")
            ]);
        }

        var preRestoreDirectory = CreatePreRestoreBackupDirectory(dryRun, actions);
        RestoreFileExact(backupDirectory, "jobs.sqlite", paths.DatabasePath, preRestoreDirectory, dryRun, actions);
        RestoreFileExact(backupDirectory, "settings.json", paths.SettingsPath, preRestoreDirectory, dryRun, actions);
        RestoreFileExact(backupDirectory, "setup-state.json", paths.SetupStatePath, preRestoreDirectory, dryRun, actions);
        RestoreFileExact(backupDirectory, "setup_report.json", paths.SetupReportPath, preRestoreDirectory, dryRun, actions);
        RestoreDirectoryExact(Path.Combine(backupDirectory, "jobs"), paths.Jobs, preRestoreDirectory, dryRun, actions);

        return new UpdateBackupRestoreResult(actions);
    }

    private string ResolveBackupDirectory(string backupNameOrPath)
    {
        if (string.Equals(backupNameOrPath, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return ListBackups().FirstOrDefault()?.Path ?? Path.Combine(paths.UpdateBackups, "latest");
        }

        if (Path.IsPathRooted(backupNameOrPath))
        {
            var fullPath = Path.GetFullPath(backupNameOrPath);
            if (!IsUnder(fullPath, paths.UpdateBackups))
            {
                throw new InvalidOperationException("Backup path must be under the KoeNote update backup directory.");
            }

            return fullPath;
        }

        if (backupNameOrPath.Contains(Path.DirectorySeparatorChar) || backupNameOrPath.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new InvalidOperationException("Backup name must not contain path separators.");
        }

        return Path.Combine(paths.UpdateBackups, backupNameOrPath);
    }

    private string CreatePreRestoreBackupDirectory(bool dryRun, List<UpdateBackupRestoreAction> actions)
    {
        var directory = CreateUniquePreRestoreDirectory();
        if (dryRun)
        {
            actions.Add(new UpdateBackupRestoreAction(directory, false, "Dry run: would save current data before restore"));
            return directory;
        }

        Directory.CreateDirectory(directory);
        BackupCurrentFile(paths.DatabasePath, Path.Combine(directory, "jobs.sqlite"), actions);
        BackupCurrentFile(paths.SettingsPath, Path.Combine(directory, "settings.json"), actions);
        BackupCurrentFile(paths.SetupStatePath, Path.Combine(directory, "setup-state.json"), actions);
        BackupCurrentFile(paths.SetupReportPath, Path.Combine(directory, "setup_report.json"), actions);
        BackupCurrentDirectory(paths.Jobs, Path.Combine(directory, "jobs"), actions);
        return directory;
    }

    private string CreateUniquePreRestoreDirectory()
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var baseName = $"pre-restore-{timestamp}";

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var directoryName = attempt == 0 ? baseName : $"{baseName}-{attempt + 1}";
            var directory = Path.Combine(paths.UpdateBackups, directoryName);
            if (!Directory.Exists(directory))
            {
                return directory;
            }
        }

        return Path.Combine(paths.UpdateBackups, $"{baseName}-{Guid.NewGuid():N}");
    }

    private static void BackupCurrentFile(string sourcePath, string destinationPath, List<UpdateBackupRestoreAction> actions)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        actions.Add(new UpdateBackupRestoreAction(destinationPath, true, "Saved current data before restore"));
    }

    private static void BackupCurrentDirectory(string sourceDirectory, string destinationDirectory, List<UpdateBackupRestoreAction> actions)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            BackupCurrentFile(sourcePath, Path.Combine(destinationDirectory, relativePath), actions);
        }
    }

    private static void RestoreFileExact(
        string backupDirectory,
        string backupRelativePath,
        string destinationPath,
        string preRestoreDirectory,
        bool dryRun,
        List<UpdateBackupRestoreAction> actions)
    {
        var sourcePath = Path.Combine(backupDirectory, backupRelativePath);
        if (!File.Exists(sourcePath))
        {
            RemoveDestinationFileMissingFromBackup(destinationPath, dryRun, actions);
            return;
        }

        if (dryRun)
        {
            actions.Add(new UpdateBackupRestoreAction(destinationPath, false, $"Dry run: would restore from {sourcePath}"));
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        actions.Add(new UpdateBackupRestoreAction(destinationPath, true, $"Restored from {sourcePath}; previous data saved under {preRestoreDirectory}"));
    }

    private static void RemoveDestinationFileMissingFromBackup(string destinationPath, bool dryRun, List<UpdateBackupRestoreAction> actions)
    {
        if (!File.Exists(destinationPath))
        {
            actions.Add(new UpdateBackupRestoreAction(destinationPath, false, "Backup file not found; destination already absent"));
            return;
        }

        if (dryRun)
        {
            actions.Add(new UpdateBackupRestoreAction(destinationPath, false, "Dry run: would delete destination because backup file is absent"));
            return;
        }

        File.Delete(destinationPath);
        actions.Add(new UpdateBackupRestoreAction(destinationPath, true, "Deleted destination because backup file is absent"));
    }

    private static void RestoreDirectoryExact(
        string sourceDirectory,
        string destinationDirectory,
        string preRestoreDirectory,
        bool dryRun,
        List<UpdateBackupRestoreAction> actions)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            RemoveDestinationDirectoryMissingFromBackup(destinationDirectory, dryRun, actions);
            return;
        }

        if (Directory.Exists(destinationDirectory))
        {
            if (dryRun)
            {
                actions.Add(new UpdateBackupRestoreAction(destinationDirectory, false, "Dry run: would replace destination directory with backup contents"));
            }
            else
            {
                Directory.Delete(destinationDirectory, recursive: true);
                actions.Add(new UpdateBackupRestoreAction(destinationDirectory, true, $"Removed existing directory before restore; previous data saved under {preRestoreDirectory}"));
            }
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            RestoreFileExact(sourceDirectory, relativePath, Path.Combine(destinationDirectory, relativePath), preRestoreDirectory, dryRun, actions);
        }
    }

    private static void RemoveDestinationDirectoryMissingFromBackup(string destinationDirectory, bool dryRun, List<UpdateBackupRestoreAction> actions)
    {
        if (!Directory.Exists(destinationDirectory))
        {
            actions.Add(new UpdateBackupRestoreAction(destinationDirectory, false, "Backup directory not found; destination already absent"));
            return;
        }

        if (dryRun)
        {
            actions.Add(new UpdateBackupRestoreAction(destinationDirectory, false, "Dry run: would delete destination directory because backup directory is absent"));
            return;
        }

        Directory.Delete(destinationDirectory, recursive: true);
        actions.Add(new UpdateBackupRestoreAction(destinationDirectory, true, "Deleted destination directory because backup directory is absent"));
    }

    private static bool IsUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        return path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }
}
