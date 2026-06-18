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
        Assert.True(Directory.Exists(paths.UpdateDownloads));
        Assert.True(Directory.Exists(paths.UpdateLogs));
        Assert.True(Directory.Exists(paths.UpdateBackups));
        Assert.True(Directory.Exists(paths.GpuRuntimes));
        Assert.False(Directory.Exists(paths.MachineModels));
    }

    [Fact]
    public void GpuRuntimePaths_UseLocalAppDataPersistentStorage()
    {
        var root = CreateTempRoot();
        var localRoot = Path.Combine(root, "local");
        var appBaseDirectory = Path.Combine(root, "app");
        var paths = new AppPaths(new AppPathOptions(
            AppDataRoot: root,
            LocalAppDataRoot: localRoot,
            AppBaseDirectory: appBaseDirectory));

        paths.EnsureCreated();

        var expectedGpuRoot = Path.Combine(localRoot, "KoeNote", "runtimes", "gpu");
        Assert.Equal(expectedGpuRoot, paths.GpuRuntimes);
        Assert.Equal(Path.Combine(expectedGpuRoot, "asr-ctranslate2-cuda"), paths.AsrCTranslate2RuntimeDirectory);
        Assert.Equal(Path.Combine(expectedGpuRoot, "review-cuda"), paths.CudaReviewRuntimeDirectory);
        Assert.Equal(Path.Combine(paths.AsrCTranslate2RuntimeDirectory, ".koenote-cuda-asr-runtime"), paths.AsrCudaRuntimeMarkerPath);
        Assert.Equal(Path.Combine(paths.CudaReviewRuntimeDirectory, ".koenote-cuda-review-runtime"), paths.CudaReviewRuntimeMarkerPath);
        Assert.DoesNotContain(appBaseDirectory, paths.AsrCTranslate2RuntimeDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(appBaseDirectory, paths.CudaReviewRuntimeDirectory, StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(paths.GpuRuntimes));
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
        Assert.True(Directory.Exists(paths.UpdateDownloads));
        Assert.True(Directory.Exists(paths.UpdateLogs));
        Assert.True(Directory.Exists(paths.UpdateBackups));
        Assert.False(Directory.Exists(paths.MachineModels));
    }

    [Fact]
    public void EnsureCreated_CreatesDomainPresetDirectoryAndCopiesBundledJsonPresets()
    {
        var root = CreateTempRoot();
        var localRoot = Path.Combine(root, "local");
        var documentsRoot = Path.Combine(root, "documents");
        var appBaseDirectory = Path.Combine(root, "app");
        var bundledPresetDirectory = Path.Combine(appBaseDirectory, "presets");
        Directory.CreateDirectory(bundledPresetDirectory);
        File.WriteAllText(Path.Combine(bundledPresetDirectory, "preset-a.json"), """{"name":"a"}""");
        File.WriteAllText(Path.Combine(bundledPresetDirectory, "preset-b.json"), """{"name":"b"}""");
        File.WriteAllText(Path.Combine(bundledPresetDirectory, "domain-preset.schema.json"), """{"name":"schema"}""");

        var paths = new AppPaths(new AppPathOptions(
            AppDataRoot: root,
            LocalAppDataRoot: localRoot,
            AppBaseDirectory: appBaseDirectory,
            DocumentsRoot: documentsRoot));

        paths.EnsureCreated();

        Assert.Equal(Path.Combine(documentsRoot, "KoeNote", "dic_preset"), paths.DomainPresetDirectory);
        Assert.True(Directory.Exists(paths.DomainPresetDirectory));
        Assert.Equal(3, Directory.EnumerateFiles(paths.DomainPresetDirectory, "*.json").Count());
        Assert.True(File.Exists(Path.Combine(paths.DomainPresetDirectory, "preset-a.json")));
        Assert.True(File.Exists(Path.Combine(paths.DomainPresetDirectory, "preset-b.json")));
        Assert.True(File.Exists(Path.Combine(paths.DomainPresetDirectory, "domain-preset.schema.json")));
    }

    [Fact]
    public void EnsureCreated_DoesNotOverwriteExistingUserDomainPreset()
    {
        var root = CreateTempRoot();
        var localRoot = Path.Combine(root, "local");
        var documentsRoot = Path.Combine(root, "documents");
        var appBaseDirectory = Path.Combine(root, "app");
        var bundledPresetDirectory = Path.Combine(appBaseDirectory, "presets");
        var userPresetDirectory = Path.Combine(documentsRoot, "KoeNote", "dic_preset");
        Directory.CreateDirectory(bundledPresetDirectory);
        Directory.CreateDirectory(userPresetDirectory);
        File.WriteAllText(Path.Combine(bundledPresetDirectory, "preset-a.json"), """{"name":"bundled"}""");
        File.WriteAllText(Path.Combine(userPresetDirectory, "preset-a.json"), """{"name":"user"}""");

        var paths = new AppPaths(new AppPathOptions(
            AppDataRoot: root,
            LocalAppDataRoot: localRoot,
            AppBaseDirectory: appBaseDirectory,
            DocumentsRoot: documentsRoot));

        paths.EnsureCreated();

        Assert.Equal("""{"name":"user"}""", File.ReadAllText(Path.Combine(paths.DomainPresetDirectory, "preset-a.json")));
    }

    private static string CreateTempRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }
}
