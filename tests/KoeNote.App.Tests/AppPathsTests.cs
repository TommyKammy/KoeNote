using KoeNote.App.Services;

namespace KoeNote.App.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void LegacyConstructor_DefaultsToCurrentUserModelStorage()
    {
        var root = CreateTempRoot();
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);

        Assert.Equal(InstallScope.CurrentUser, paths.InstallScope);
        Assert.Equal(paths.UserModels, paths.DefaultModelStorageRoot);
    }

    [Fact]
    public void EnsureCreated_CurrentUserScope_DoesNotCreateMachineModelStorage()
    {
        var root = CreateTempRoot();
        var localRoot = Path.Combine(root, "local");
        var programDataRoot = Path.Combine(root, "program-data");
        var paths = new AppPaths(new AppPathOptions(
            AppDataRoot: root,
            LocalAppDataRoot: localRoot,
            ProgramDataRoot: programDataRoot,
            AppBaseDirectory: AppContext.BaseDirectory,
            InstallScope: InstallScope.CurrentUser));

        paths.EnsureCreated();

        Assert.Equal(paths.UserModels, paths.DefaultModelStorageRoot);
        Assert.True(Directory.Exists(paths.UserModels));
        Assert.True(Directory.Exists(paths.UpdateBackups));
        Assert.False(Directory.Exists(paths.MachineModels));
    }

    [Fact]
    public void EnsureCreated_AllUsersScope_DoesNotRequireMachineModelStorageAtStartup()
    {
        var root = CreateTempRoot();
        var localRoot = Path.Combine(root, "local");
        var programDataRoot = Path.Combine(root, "program-data");
        var paths = new AppPaths(new AppPathOptions(
            AppDataRoot: root,
            LocalAppDataRoot: localRoot,
            ProgramDataRoot: programDataRoot,
            AppBaseDirectory: AppContext.BaseDirectory,
            InstallScope: InstallScope.AllUsers));

        paths.EnsureCreated();

        Assert.Equal(paths.MachineModels, paths.DefaultModelStorageRoot);
        Assert.True(Directory.Exists(paths.UserModels));
        Assert.True(Directory.Exists(paths.UpdateBackups));
        Assert.False(Directory.Exists(paths.MachineModels));
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }
}
