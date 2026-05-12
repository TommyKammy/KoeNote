using KoeNote.Cleanup;

namespace KoeNote.App.Tests;

public sealed class CleanupServiceTests
{
    [Fact]
    public void CleanupOptions_InteractiveWithoutExplicitTargets_DefaultsToNoDestructiveDataRemoval()
    {
        var options = CleanupOptions.Parse([]);

        Assert.False(options.Quiet);
        Assert.False(options.HasExplicitTargets);
        Assert.False(options.RemoveAllData);
        Assert.False(options.RemoveLogs);
        Assert.False(options.RemoveDownloads);
        Assert.False(options.RemoveUserModels);
        Assert.False(options.RemoveMachineModels);
        Assert.False(options.RemoveUserData);
    }

    [Fact]
    public void CleanupOptions_QuietWithoutExplicitTargets_DefaultsToAppOnly()
    {
        var options = CleanupOptions.Parse(["--quiet"]);

        Assert.True(options.Quiet);
        Assert.False(options.HasExplicitTargets);
        Assert.False(options.RemoveAllData);
        Assert.False(options.RemoveLogs);
        Assert.False(options.RemoveDownloads);
        Assert.False(options.RemoveUserModels);
        Assert.False(options.RemoveMachineModels);
        Assert.False(options.RemoveUserData);
    }

    [Fact]
    public void CleanupOptions_QuietWithExplicitTargets_DoesNotAddDefaultTargets()
    {
        var options = CleanupOptions.Parse(["--quiet", "--models"]);

        Assert.True(options.Quiet);
        Assert.True(options.HasExplicitTargets);
        Assert.False(options.RemoveAllData);
        Assert.False(options.RemoveLogs);
        Assert.False(options.RemoveDownloads);
        Assert.True(options.RemoveUserModels);
        Assert.False(options.RemoveMachineModels);
        Assert.False(options.RemoveUserData);
    }

    [Fact]
    public void CleanupOptions_ParseAllData()
    {
        var options = CleanupOptions.Parse(["--quiet", "--all"]);

        Assert.True(options.Quiet);
        Assert.True(options.HasExplicitTargets);
        Assert.True(options.RemoveAllData);
        Assert.False(options.RemoveLogs);
        Assert.False(options.RemoveDownloads);
        Assert.False(options.RemoveUserModels);
        Assert.False(options.RemoveMachineModels);
        Assert.False(options.RemoveUserData);
        Assert.True(options.ToPlan().RemoveAllData);
    }

    [Fact]
    public void CleanupOptions_ParseRestoreUpdateBackup()
    {
        var options = CleanupOptions.Parse(["--restore-update-backup", "latest", "--dry-run"]);

        Assert.True(options.DryRun);
        Assert.Equal("latest", options.RestoreUpdateBackup);
    }

    [Fact]
    public void CleanupOptions_ParseListUpdateBackups()
    {
        var options = CleanupOptions.Parse(["--list-update-backups"]);

        Assert.True(options.ListUpdateBackups);
    }

    [Fact]
    public void Execute_AllDataPolicy_RemovesOnlyKoeNoteDataRootsAndKeepsDocumentExports()
    {
        var root = CreateTempRoot();
        var documentsRoot = Path.Combine(root, "documents");
        var exports = Path.Combine(documentsRoot, "KoeNote", "Exports", "transcript.md");
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        CreateFile(paths.DatabasePath);
        CreateFile(Path.Combine(paths.Jobs, "job-1", "result.json"));
        CreateFile(Path.Combine(paths.Logs, "app.log"));
        CreateFile(Path.Combine(paths.ModelDownloads, "download.tmp"));
        CreateFile(Path.Combine(paths.UpdateDownloads, "KoeNote.msi"));
        CreateFile(Path.Combine(paths.UpdateBackups, "schema-backup", "jobs.sqlite"));
        CreateFile(Path.Combine(paths.PythonPackages, "package.txt"));
        CreateFile(Path.Combine(paths.PythonEnvironments, "asr", "pyvenv.cfg"));
        CreateFile(Path.Combine(paths.UserModels, "asr", "model.bin"));
        CreateFile(Path.Combine(paths.MachineModels, "review", "model.gguf"));
        CreateFile(exports, "exported transcript");

        var service = new CleanupService(paths);
        var result = service.Execute(CleanupPlan.AllData, dryRun: false);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(paths.UserRoot));
        Assert.False(Directory.Exists(paths.LocalRoot));
        Assert.False(Directory.Exists(paths.MachineRoot));
        Assert.True(File.Exists(exports));
    }

    [Fact]
    public void Execute_AllDataDryRun_DoesNotRemoveKoeNoteDataRoots()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        CreateFile(paths.DatabasePath);
        CreateFile(Path.Combine(paths.UserModels, "asr", "model.bin"));
        CreateFile(Path.Combine(paths.MachineModels, "review", "model.gguf"));

        var service = new CleanupService(paths);
        var result = service.Execute(CleanupPlan.AllData, dryRun: true);

        Assert.True(result.Succeeded);
        Assert.True(Directory.Exists(paths.UserRoot));
        Assert.True(Directory.Exists(paths.LocalRoot));
        Assert.True(Directory.Exists(paths.MachineRoot));
    }

    [Fact]
    public void Execute_EphemeralPolicy_RemovesLogsAndDownloadsButKeepsModelsAndUserData()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        CreateFile(Path.Combine(paths.Logs, "app.log"));
        CreateFile(Path.Combine(paths.ModelDownloads, "download.tmp"));
        CreateFile(Path.Combine(paths.UserModels, "asr", "model.bin"));
        CreateFile(paths.DatabasePath);
        CreateFile(paths.SetupStatePath);

        var service = new CleanupService(paths);
        var result = service.Execute(new CleanupPlan(
            RemoveLogs: true,
            RemoveDownloads: true,
            RemoveUserModels: false,
            RemoveMachineModels: false,
            RemoveUserData: false), dryRun: false);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(paths.Logs));
        Assert.False(Directory.Exists(paths.ModelDownloads));
        Assert.True(Directory.Exists(paths.UserModels));
        Assert.True(File.Exists(paths.DatabasePath));
        Assert.True(File.Exists(paths.SetupStatePath));
    }

    [Fact]
    public void Execute_UserDataPolicy_RemovesJobsSettingsAndSetupState()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        CreateFile(Path.Combine(paths.Jobs, "job-1", "result.json"));
        CreateFile(paths.DatabasePath);
        CreateFile(paths.SettingsPath);
        CreateFile(paths.SetupStatePath);
        CreateFile(paths.SetupReportPath);

        var service = new CleanupService(paths);
        var result = service.Execute(new CleanupPlan(
            RemoveLogs: false,
            RemoveDownloads: false,
            RemoveUserModels: false,
            RemoveMachineModels: false,
            RemoveUserData: true), dryRun: false);

        Assert.True(result.Succeeded);
        Assert.False(Directory.Exists(paths.Jobs));
        Assert.False(File.Exists(paths.DatabasePath));
        Assert.False(File.Exists(paths.SettingsPath));
        Assert.False(File.Exists(paths.SetupStatePath));
        Assert.False(File.Exists(paths.SetupReportPath));
    }

    [Fact]
    public void UpdateBackupRestoreService_ListBackups_ReturnsNewestFirst()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        var older = Directory.CreateDirectory(Path.Combine(paths.UpdateBackups, "older"));
        var newer = Directory.CreateDirectory(Path.Combine(paths.UpdateBackups, "newer"));
        var preRestore = Directory.CreateDirectory(Path.Combine(paths.UpdateBackups, "pre-restore-latest"));
        older.LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(-10);
        newer.LastWriteTimeUtc = DateTime.UtcNow;
        preRestore.LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(10);

        var backups = new UpdateBackupRestoreService(paths).ListBackups();

        Assert.Equal(["newer", "older"], backups.Select(static backup => backup.Name).ToArray());
    }

    [Fact]
    public void UpdateBackupRestoreService_RestoreLatest_RestoresDataAndSavesCurrentData()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        var backup = Path.Combine(paths.UpdateBackups, "schema-1-to-14-test");
        CreateFile(Path.Combine(backup, "jobs.sqlite"), "backup database");
        CreateFile(Path.Combine(backup, "settings.json"), "backup settings");
        CreateFile(Path.Combine(backup, "jobs", "job-1", "result.json"), "backup job");
        CreateFile(paths.DatabasePath, "current database");
        CreateFile(paths.SettingsPath, "current settings");
        CreateFile(Path.Combine(paths.Jobs, "job-1", "result.json"), "current job");

        var result = new UpdateBackupRestoreService(paths).Restore("latest", dryRun: false);

        Assert.True(result.Succeeded);
        Assert.Equal("backup database", File.ReadAllText(paths.DatabasePath));
        Assert.Equal("backup settings", File.ReadAllText(paths.SettingsPath));
        Assert.Equal("backup job", File.ReadAllText(Path.Combine(paths.Jobs, "job-1", "result.json")));
        var preRestore = Assert.Single(Directory.EnumerateDirectories(paths.UpdateBackups, "pre-restore-*"));
        Assert.Equal("current database", File.ReadAllText(Path.Combine(preRestore, "jobs.sqlite")));
        Assert.Equal("current settings", File.ReadAllText(Path.Combine(preRestore, "settings.json")));
        Assert.Equal("current job", File.ReadAllText(Path.Combine(preRestore, "jobs", "job-1", "result.json")));
    }

    [Fact]
    public void UpdateBackupRestoreService_RestoreLatest_DoesNotSelectPreRestoreDirectory()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        var backup = Directory.CreateDirectory(Path.Combine(paths.UpdateBackups, "schema-1-to-14-test"));
        var preRestore = Directory.CreateDirectory(Path.Combine(paths.UpdateBackups, "pre-restore-newer"));
        backup.LastWriteTimeUtc = DateTime.UtcNow;
        preRestore.LastWriteTimeUtc = DateTime.UtcNow.AddMinutes(10);
        CreateFile(Path.Combine(backup.FullName, "settings.json"), "real backup settings");
        CreateFile(Path.Combine(preRestore.FullName, "settings.json"), "pre restore settings");

        var result = new UpdateBackupRestoreService(paths).Restore("latest", dryRun: false);

        Assert.True(result.Succeeded);
        Assert.Equal("real backup settings", File.ReadAllText(paths.SettingsPath));
    }

    [Fact]
    public void UpdateBackupRestoreService_Restore_RemovesCurrentFilesMissingFromBackup()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        var backup = Path.Combine(paths.UpdateBackups, "schema-1-to-14-test");
        CreateFile(Path.Combine(backup, "settings.json"), "backup settings");
        CreateFile(paths.SetupReportPath, "current report");
        CreateFile(Path.Combine(paths.Jobs, "stale-job", "result.json"), "stale job");

        var result = new UpdateBackupRestoreService(paths).Restore("schema-1-to-14-test", dryRun: false);

        Assert.True(result.Succeeded);
        Assert.False(File.Exists(paths.SetupReportPath));
        Assert.False(Directory.Exists(paths.Jobs));
        var preRestore = Assert.Single(Directory.EnumerateDirectories(paths.UpdateBackups, "pre-restore-*"));
        Assert.Equal("current report", File.ReadAllText(Path.Combine(preRestore, "setup_report.json")));
        Assert.Equal("stale job", File.ReadAllText(Path.Combine(preRestore, "jobs", "stale-job", "result.json")));
    }

    [Fact]
    public void UpdateBackupRestoreService_Restore_ReplacesCurrentJobDirectoryWithBackupContents()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        var backup = Path.Combine(paths.UpdateBackups, "schema-1-to-14-test");
        CreateFile(Path.Combine(backup, "jobs", "job-1", "result.json"), "backup job");
        CreateFile(Path.Combine(paths.Jobs, "stale-job", "result.json"), "stale job");

        var result = new UpdateBackupRestoreService(paths).Restore("schema-1-to-14-test", dryRun: false);

        Assert.True(result.Succeeded);
        Assert.Equal("backup job", File.ReadAllText(Path.Combine(paths.Jobs, "job-1", "result.json")));
        Assert.False(File.Exists(Path.Combine(paths.Jobs, "stale-job", "result.json")));
    }

    [Fact]
    public void UpdateBackupRestoreService_Restore_RejectsPathOutsideUpdateBackups()
    {
        var root = CreateTempRoot();
        var paths = new CleanupPaths(
            Path.Combine(root, "roaming"),
            Path.Combine(root, "local"),
            Path.Combine(root, "program-data"));
        var outsidePath = Path.Combine(root, "outside-backup");

        Assert.Throws<InvalidOperationException>(() =>
            new UpdateBackupRestoreService(paths).Restore(outsidePath, dryRun: true));
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private static void CreateFile(string path)
    {
        CreateFile(path, "test");
    }

    private static void CreateFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }
}
