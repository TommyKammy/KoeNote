using System.IO;

namespace KoeNote.Cleanup;

public sealed class CleanupService(CleanupPaths paths)
{
    public IReadOnlyList<CleanupTarget> BuildTargets(CleanupPlan plan)
    {
        return
        [
            new("logs", paths.Logs, true, plan.RemoveLogs),
            new("temporary model downloads", paths.ModelDownloads, true, plan.RemoveDownloads),
            new("optional Python packages", paths.PythonPackages, true, plan.RemoveDownloads),
            new("downloaded user models", paths.UserModels, true, plan.RemoveUserModels),
            new("shared machine models", paths.MachineModels, true, plan.RemoveMachineModels),
            new("jobs folder", paths.Jobs, true, plan.RemoveUserData),
            new("jobs database", paths.DatabasePath, false, plan.RemoveUserData),
            new("settings", paths.SettingsPath, false, plan.RemoveUserData),
            new("setup state", paths.SetupStatePath, false, plan.RemoveUserData),
            new("setup report", paths.SetupReportPath, false, plan.RemoveUserData)
        ];
    }

    public CleanupResult Execute(CleanupPlan plan, bool dryRun)
    {
        var actions = new List<CleanupActionResult>();

        foreach (var target in BuildTargets(plan))
        {
            if (!target.Remove)
            {
                actions.Add(new CleanupActionResult(target.Path, false, "not selected"));
                continue;
            }

            if (!IsSafeKnownPath(target.Path))
            {
                actions.Add(new CleanupActionResult(target.Path, false, "Failed: path is outside KoeNote data roots"));
                continue;
            }

            if (!Exists(target))
            {
                actions.Add(new CleanupActionResult(target.Path, false, "already absent"));
                continue;
            }

            if (dryRun)
            {
                actions.Add(new CleanupActionResult(target.Path, false, "dry run"));
                continue;
            }

            try
            {
                if (target.IsDirectory)
                {
                    Directory.Delete(target.Path, recursive: true);
                }
                else
                {
                    File.Delete(target.Path);
                }

                actions.Add(new CleanupActionResult(target.Path, true, "deleted"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                actions.Add(new CleanupActionResult(target.Path, false, $"Failed: {ex.Message}"));
            }
        }

        RemoveEmptyKoeNoteDataDirectories(dryRun, actions);
        return new CleanupResult(actions);
    }

    private static bool Exists(CleanupTarget target)
    {
        return target.IsDirectory ? Directory.Exists(target.Path) : File.Exists(target.Path);
    }

    private bool IsSafeKnownPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return IsUnder(fullPath, Path.Combine(paths.AppDataRoot, "KoeNote")) ||
            IsUnder(fullPath, Path.Combine(paths.LocalAppDataRoot, "KoeNote")) ||
            IsUnder(fullPath, Path.Combine(paths.ProgramDataRoot, "KoeNote"));
    }

    private static bool IsUnder(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root);
        if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            fullRoot += Path.DirectorySeparatorChar;
        }

        return path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), fullRoot.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveEmptyKoeNoteDataDirectories(bool dryRun, List<CleanupActionResult> actions)
    {
        foreach (var directory in new[]
        {
            Path.Combine(paths.LocalAppDataRoot, "KoeNote"),
            Path.Combine(paths.AppDataRoot, "KoeNote"),
            Path.Combine(paths.ProgramDataRoot, "KoeNote")
        })
        {
            try
            {
                if (dryRun || !Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    continue;
                }

                Directory.Delete(directory);
                actions.Add(new CleanupActionResult(directory, true, "empty data directory deleted"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                actions.Add(new CleanupActionResult(directory, false, $"Failed: {ex.Message}"));
            }
        }
    }
}
