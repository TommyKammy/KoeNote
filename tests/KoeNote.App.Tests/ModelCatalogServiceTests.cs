using KoeNote.App.Services;
using KoeNote.App.Services.Models;
using System.Net;
using System.Net.Http;

namespace KoeNote.App.Tests;

public sealed class ModelCatalogServiceTests
{
    [Fact]
    public void LoadBuiltInCatalog_ReturnsAsrAndReviewModels()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var entries = new ModelCatalogService(paths).ListEntries();

        Assert.Contains(entries, entry => entry.ModelId == "reazonspeech-k2-v3-ja" && entry.Role == "asr");
        Assert.Contains(entries, entry => entry.ModelId == "faster-whisper-large-v3-turbo" && entry.Role == "asr");
        Assert.Contains(entries, entry => entry.Role == "review");
    }

    [Fact]
    public void RegisterLocalModel_AddsInstalledStateToCatalogEntry()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogService = new ModelCatalogService(paths);
        var repository = new InstalledModelRepository(paths);
        var installService = new ModelInstallService(paths, repository, new ModelVerificationService());
        var modelPath = Path.Combine(paths.UserModels, "asr", "dummy.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "dummy model");
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "vibevoice-asr-q4-k");

        installService.RegisterLocalModel(catalogItem, modelPath, "local_file");

        var entry = catalogService.ListEntries().First(entry => entry.ModelId == "vibevoice-asr-q4-k");
        Assert.True(entry.IsInstalled);
        Assert.True(entry.IsVerified);
        Assert.Equal("installed", entry.InstallState);
        Assert.Contains("VRAM", entry.RuntimeRequirement, StringComparison.Ordinal);
        Assert.NotEmpty(entry.LicenseName);
    }

    [Fact]
    public void LoadCatalogFile_LoadsLocalCatalog()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogPath = Path.Combine(paths.Root, "local-catalog.json");
        File.WriteAllText(catalogPath, ModelCatalogService.Serialize(new ModelCatalog(
            1,
            "local",
            [
                new ModelCatalogItem(
                    "local-model",
                    "local",
                    "asr",
                    "vibevoice-crispasr",
                    "Local Model",
                    ["ja"],
                    [],
                    new ModelRuntimeSpec("local-runtime", "runtime-local"),
                    new ModelDownloadSpec("local", null, null),
                    new ModelLicenseSpec("Local license", null),
                    new ModelRequirements(false, 0, false),
                    "available")
            ])));

        var catalog = new ModelCatalogService(paths).LoadCatalogFile(catalogPath);

        Assert.Equal("local", catalog.CatalogVersion);
        Assert.Equal("local-model", Assert.Single(catalog.Models).ModelId);
    }

    [Fact]
    public async Task LoadRemoteCatalogAsync_RequiresHttpsAndParsesCatalog()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var json = ModelCatalogService.Serialize(new ModelCatalog(1, "remote", []));
        using var httpClient = new HttpClient(new StubHandler(json));

        var catalog = await new ModelCatalogService(paths).LoadRemoteCatalogAsync(new Uri("https://example.com/catalog.json"), httpClient);

        Assert.Equal("remote", catalog.CatalogVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ModelCatalogService(paths).LoadRemoteCatalogAsync(new Uri("http://example.com/catalog.json"), httpClient));
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }
}
