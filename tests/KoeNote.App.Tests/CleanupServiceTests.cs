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
        Assert.False(options.RemoveLogs);
        Assert.False(options.RemoveDownloads);
        Assert.False(options.RemoveUserModels);
        Assert.False(options.RemoveMachineModels);
        Assert.False(options.RemoveUserData);
    }

    [Fact]
    public void CleanupOptions_QuietWithoutExplicitTargets_RemovesOnlyEphemeralData()
    {
        var options = CleanupOptions.Parse(["--quiet"]);

        Assert.True(options.Quiet);
        Assert.False(options.HasExplicitTargets);
        Assert.True(options.RemoveLogs);
        Assert.True(options.RemoveDownloads);
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
        Assert.False(options.RemoveLogs);
        Assert.False(options.RemoveDownloads);
        Assert.True(options.RemoveUserModels);
        Assert.False(options.RemoveMachineModels);
        Assert.False(options.RemoveUserData);
    }

    [Fact]
    public void Execute_DefaultQuietPolicy_RemovesLogsAndDownloadsButKeepsModelsAndUserData()
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

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private static void CreateFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "test");
    }
}
