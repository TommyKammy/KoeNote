using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services;

public sealed record UpdateBackupResult(string BackupDirectory, IReadOnlyList<string> CopiedPaths);

public sealed class UpdateBackupService(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public UpdateBackupResult CreateBeforeMigrationBackup(int currentSchemaVersion, int targetSchemaVersion)
    {
        Directory.CreateDirectory(paths.UpdateBackups);

        var backupDirectory = CreateUniqueBackupDirectory(currentSchemaVersion, targetSchemaVersion);
        var timestamp = DateTimeOffset.Now.ToString("o");
        Directory.CreateDirectory(backupDirectory);

        var copied = new List<string>();
        CopyDatabase(backupDirectory, copied);
        CopyFileIfExists(paths.SettingsPath, Path.Combine(backupDirectory, "settings.json"), copied);
        CopyFileIfExists(paths.SetupStatePath, Path.Combine(backupDirectory, "setup-state.json"), copied);
        CopyFileIfExists(paths.SetupReportPath, Path.Combine(backupDirectory, "setup_report.json"), copied);
        CopyDirectoryIfExists(paths.Jobs, Path.Combine(backupDirectory, "jobs"), copied);

        var manifest = new
        {
            created_at = timestamp,
            current_schema_version = currentSchemaVersion,
            target_schema_version = targetSchemaVersion,
            source_root = paths.Root,
            copied_paths = copied
        };
        File.WriteAllText(
            Path.Combine(backupDirectory, "backup-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));

        return new UpdateBackupResult(backupDirectory, copied);
    }

    private string CreateUniqueBackupDirectory(int currentSchemaVersion, int targetSchemaVersion)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
        var baseName = $"schema-{currentSchemaVersion}-to-{targetSchemaVersion}-{timestamp}";

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var directoryName = attempt == 0 ? baseName : $"{baseName}-{attempt + 1}";
            var backupDirectory = Path.Combine(paths.UpdateBackups, directoryName);
            if (!Directory.Exists(backupDirectory))
            {
                return backupDirectory;
            }
        }

        return Path.Combine(paths.UpdateBackups, $"{baseName}-{Guid.NewGuid():N}");
    }

    private void CopyDatabase(string backupDirectory, List<string> copied)
    {
        if (!File.Exists(paths.DatabasePath))
        {
            return;
        }

        var backupPath = Path.Combine(backupDirectory, "jobs.sqlite");
        using var source = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        using var destination = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath
        }.ToString());

        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
        copied.Add(backupPath);
    }

    private static void CopyFileIfExists(string sourcePath, string destinationPath, List<string> copied)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
        copied.Add(destinationPath);
    }

    private static void CopyDirectoryIfExists(string sourceDirectory, string destinationDirectory, List<string> copied)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            return;
        }

        foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, sourcePath);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            CopyFileIfExists(sourcePath, destinationPath, copied);
        }
    }
}
