using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class ModelInstallServiceTests
{
    [Fact]
    public void DeleteModelFiles_RemovesStoredModelAndRegistration()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new InstalledModelRepository(paths);
        var service = new ModelInstallService(paths, repository, new ModelVerificationService());
        var catalogItem = CreateCatalogItem("delete-model");
        var modelPath = Path.Combine(paths.UserModels, "asr", catalogItem.ModelId);
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "model.bin"), "model data");
        service.RegisterLocalModel(catalogItem, modelPath, "download");

        var result = service.DeleteModelFiles(catalogItem.ModelId);

        Assert.True(result.DeletedRegistration);
        Assert.False(Directory.Exists(modelPath));
        Assert.Null(repository.FindInstalledModel(catalogItem.ModelId));
        Assert.True(result.DeletedBytes > 0);
    }

    [Fact]
    public void DeleteModelFiles_RemovesDownloadedModelFromCustomStorage()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new InstalledModelRepository(paths);
        var service = new ModelInstallService(paths, repository, new ModelVerificationService());
        var catalogItem = CreateCatalogItem("custom-delete-model");
        var customRoot = Path.Combine(paths.Root, "custom-models");
        var modelPath = Path.Combine(customRoot, "asr", catalogItem.ModelId);
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "model.bin"), "model data");
        service.RegisterLocalModel(catalogItem, modelPath, "download");

        var result = service.DeleteModelFiles(catalogItem.ModelId);

        Assert.True(result.DeletedRegistration);
        Assert.False(Directory.Exists(modelPath));
        Assert.True(Directory.Exists(customRoot));
        Assert.Null(repository.FindInstalledModel(catalogItem.ModelId));
    }

    [Fact]
    public void DeleteModelFiles_RejectsRegisteredPathOutsideModelStorage()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new InstalledModelRepository(paths);
        var service = new ModelInstallService(paths, repository, new ModelVerificationService());
        var catalogItem = CreateCatalogItem("external-model");
        var externalPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "model.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(externalPath)!);
        File.WriteAllText(externalPath, "external model");
        service.RegisterLocalModel(catalogItem, externalPath, "local_file");

        Assert.Throws<InvalidOperationException>(() => service.DeleteModelFiles(catalogItem.ModelId));

        Assert.True(File.Exists(externalPath));
        Assert.NotNull(repository.FindInstalledModel(catalogItem.ModelId));
    }

    private static ModelCatalogItem CreateCatalogItem(string modelId)
    {
        return new ModelCatalogItem(
            modelId,
            "test",
            "asr",
            "test-engine",
            "Delete Model",
            ["ja"],
            [],
            new ModelRuntimeSpec("test-runtime", "runtime-test"),
            new ModelDownloadSpec("https", "https://example.com/model.bin", null),
            new ModelLicenseSpec("Test license", null),
            new ModelRequirements(false, 0, false),
            "available");
    }
}
