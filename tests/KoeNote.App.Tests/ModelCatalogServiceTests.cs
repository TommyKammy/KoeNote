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

        var asrModels = entries.Where(entry => entry.Role == "asr").Select(entry => entry.ModelId).ToArray();
        Assert.Equal(
            ["faster-whisper-large-v3", "faster-whisper-large-v3-turbo", "kotoba-whisper-v2.2-faster", "whisper-base", "whisper-small"],
            asrModels.Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.Contains(entries, entry => entry.ModelId == "whisper-base" && entry.Role == "asr" && entry.QualityLabelSummary.Contains("軽量", StringComparison.Ordinal));
        Assert.Contains(entries, entry => entry.ModelId == "kotoba-whisper-v2.2-faster" && entry.Role == "asr" && entry.IsDirectDownloadSupported);
        Assert.Contains(entries, entry => entry.ModelId == "faster-whisper-large-v3-turbo" && entry.Role == "asr" && entry.IsDirectDownloadSupported);
        Assert.Contains(entries, entry => entry.ModelId == "llm-jp-4-8b-thinking-q4-k-m" && entry.Role == "review" && entry.IsDirectDownloadSupported);
        Assert.Contains(entries, entry => entry.ModelId == "bonsai-8b-q1-0" && entry.Role == "review" && entry.IsDirectDownloadSupported && entry.CatalogItem.Status == "available");
        Assert.Contains(entries, entry => entry.ModelId == "gemma-4-e4b-it-q4-k-m" && entry.Role == "review" && entry.CatalogItem.OutputSanitizerProfile == "markdownSectionOnly");
        Assert.DoesNotContain(entries, entry => entry.ModelId == "ternary-bonsai-8b-q2-0");
        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        Assert.Contains(catalog.Models, model => model.ModelId == "ternary-bonsai-8b-q2-0" && model.Status == "hidden");
    }

    [Fact]
    public void LoadBuiltInCatalog_ReturnsQualityPresets()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        var presets = catalog.Presets ?? [];

        Assert.Equal(["experimental", "high_accuracy", "lightweight", "recommended", "ultra_lightweight"], presets.Select(preset => preset.PresetId).Order(StringComparer.Ordinal).ToArray());

        var ultraLightweight = presets.Single(preset => preset.PresetId == "ultra_lightweight");
        Assert.Equal("whisper-base", ultraLightweight.AsrModelId);
        Assert.Equal("bonsai-8b-q1-0", ultraLightweight.ReviewModelId);

        var lightweight = presets.Single(preset => preset.PresetId == "lightweight");
        Assert.Equal("軽量", lightweight.QualityLabel);
        Assert.Equal("whisper-small", lightweight.AsrModelId);
        Assert.Equal("bonsai-8b-q1-0", lightweight.ReviewModelId);
        Assert.DoesNotContain("Reviewは実験的", lightweight.Badges);

        var recommended = presets.Single(preset => preset.PresetId == "recommended");
        Assert.Equal("推奨", recommended.QualityLabel);
        Assert.Equal("faster-whisper-large-v3-turbo", recommended.AsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", recommended.ReviewModelId);

        var highAccuracy = presets.Single(preset => preset.PresetId == "high_accuracy");
        Assert.Equal("高精度", highAccuracy.QualityLabel);
        Assert.Equal("kotoba-whisper-v2.2-faster", highAccuracy.AsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", highAccuracy.ReviewModelId);

        var experimental = presets.Single(preset => preset.PresetId == "experimental");
        Assert.Equal("実験的", experimental.QualityLabel);
        Assert.Equal("faster-whisper-large-v3-turbo", experimental.AsrModelId);
        Assert.Equal("llm-jp-4-8b-thinking-q4-k-m", experimental.ReviewModelId);
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
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "kotoba-whisper-v2.2-faster");

        installService.RegisterLocalModel(catalogItem, modelPath, "local_file");

        var entry = catalogService.ListEntries().First(entry => entry.ModelId == "kotoba-whisper-v2.2-faster");
        Assert.True(entry.IsInstalled);
        Assert.True(entry.IsVerified);
        Assert.Equal("installed", entry.InstallState);
        Assert.Contains("VRAM", entry.RuntimeRequirement, StringComparison.Ordinal);
        Assert.NotEmpty(entry.LicenseName);
    }

    [Fact]
    public void ListEntries_TreatsInstalledRecordWithMissingFilesAsMissing()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogService = new ModelCatalogService(paths);
        var installService = new ModelInstallService(paths, new InstalledModelRepository(paths), new ModelVerificationService());
        var modelPath = Path.Combine(paths.UserModels, "asr", "faster-whisper-large-v3");
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "model.bin"), "dummy model");
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "faster-whisper-large-v3");
        installService.RegisterLocalModel(catalogItem, modelPath, "download");
        Directory.Delete(modelPath, recursive: true);

        var entry = catalogService.ListEntries().First(entry => entry.ModelId == "faster-whisper-large-v3");

        Assert.False(entry.IsInstalled);
        Assert.False(entry.IsVerified);
        Assert.Equal("missing", entry.InstallState);
        Assert.Equal("faster-whisper large-v3", entry.SetupDisplayName);
    }

    [Fact]
    public void ListEntries_IncludesLatestDownloadState()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogItem = new ModelCatalogService(paths)
            .LoadBuiltInCatalog()
            .Models
            .First(model => model.ModelId == "faster-whisper-large-v3-turbo");
        var repository = new ModelDownloadJobRepository(paths);
        var downloadId = repository.Start(
            catalogItem.ModelId,
            "https://example.com/model.bin",
            Path.Combine(paths.UserModels, "asr", "model.bin"),
            Path.Combine(paths.UserModels, "asr", "model.bin.partial"),
            null);
        repository.UpdateProgress(downloadId, 512, 1024);
        repository.MarkPaused(downloadId);

        var entry = new ModelCatalogService(paths)
            .ListEntries()
            .First(entry => entry.ModelId == catalogItem.ModelId);

        Assert.Equal("paused", entry.DownloadState);
        Assert.Contains("50%", entry.DownloadProgressSummary, StringComparison.Ordinal);
        Assert.Equal(50, entry.DownloadProgressPercent);
        Assert.True(entry.HasKnownDownloadProgress);
        Assert.True(entry.IsDownloadActive);
        Assert.Contains("512 B / 1 KB", entry.DownloadBytesSummary, StringComparison.Ordinal);
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
                    "local-asr",
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
