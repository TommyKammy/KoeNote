using System.IO.Compression;
using KoeNote.App.Services;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class ModelPackImportServiceTests
{
    [Fact]
    public void ImportModelPack_ExtractsAndRegistersModel()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var packPath = Path.Combine(paths.Root, "pack.kmodelpack");
        CreatePack(packPath);
        var catalog = new ModelCatalogService(paths);
        var verification = new ModelVerificationService();
        var install = new ModelInstallService(paths, new InstalledModelRepository(paths), verification);
        var importer = new ModelPackImportService(paths, catalog, install);

        var installed = importer.ImportModelPack(packPath);

        var model = Assert.Single(installed);
        Assert.Equal("vibevoice-asr-q4-k", model.ModelId);
        Assert.Equal("model_pack", model.SourceType);
        Assert.True(model.Verified);
    }

    [Fact]
    public void ImportModelPack_DoesNotReplaceExistingPackWhenVerificationFails()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var packPath = Path.Combine(paths.Root, "pack.kmodelpack");
        CreatePack(packPath);
        var catalog = new ModelCatalogService(paths);
        var verification = new ModelVerificationService();
        var install = new ModelInstallService(paths, new InstalledModelRepository(paths), verification);
        var importer = new ModelPackImportService(paths, catalog, install);
        importer.ImportModelPack(packPath);
        var installedBefore = new InstalledModelRepository(paths).FindInstalledModel("vibevoice-asr-q4-k");
        Assert.NotNull(installedBefore);
        var existingPath = installedBefore.FilePath;
        Assert.True(File.Exists(existingPath));

        File.Delete(packPath);
        CreatePack(packPath, badSha256: true);

        Assert.Throws<InvalidOperationException>(() => importer.ImportModelPack(packPath));

        Assert.True(File.Exists(existingPath));
        var installedAfter = new InstalledModelRepository(paths).FindInstalledModel("vibevoice-asr-q4-k");
        Assert.Equal(existingPath, installedAfter?.FilePath);
    }

    private static void CreatePack(string packPath, bool badSha256 = false)
    {
        var sourceRoot = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var modelPath = Path.Combine(sourceRoot, "models", "vibevoice-asr-q4-k", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "packed model");
        var sha256 = badSha256 ? new string('0', 64) : ModelVerificationService.ComputeSha256(modelPath);
        File.WriteAllText(Path.Combine(sourceRoot, "modelpack.json"), $$"""
            {
              "schema_version": 1,
              "pack_id": "test-pack",
              "display_name": "Test Pack",
              "models": [
                {
                  "model_id": "vibevoice-asr-q4-k",
                  "engine_id": "vibevoice-crispasr",
                  "relative_path": "models/vibevoice-asr-q4-k/model.gguf",
                  "sha256": "{{sha256}}"
                }
              ]
            }
            """);
        ZipFile.CreateFromDirectory(sourceRoot, packPath);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }
}
