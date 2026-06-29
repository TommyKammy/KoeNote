using System.Net;
using System.Net.Http;
using KoeNote.App.Services;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;

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
        Assert.Contains(entries, entry => entry.ModelId == "whisper-base" && entry.Role == "asr");
        Assert.Contains(entries, entry => entry.ModelId == "kotoba-whisper-v2.2-faster" && entry.Role == "asr" && entry.IsDirectDownloadSupported);
        Assert.Contains(entries, entry => entry.ModelId == "faster-whisper-large-v3-turbo" && entry.Role == "asr" && entry.IsDirectDownloadSupported);
        Assert.Contains(entries, entry => entry.ModelId == "llm-jp-4-8b-thinking-q4-k-m" && entry.Role == "review" && entry.IsDirectDownloadSupported);
        Assert.Contains(entries, entry => entry.ModelId == "bonsai-8b-q1-0" && entry.Role == "review" && entry.IsDirectDownloadSupported && entry.CatalogItem.Status == "available");
        Assert.Contains(entries, entry => entry.ModelId == "gemma-4-e4b-it-q4-k-m" && entry.Role == "review" && entry.CatalogItem.OutputSanitizerProfile == "markdownSectionOnly");
        Assert.Contains(entries, entry => entry.ModelId == Gemma12BLocalValidation.ModelId && entry.Role == "review" && entry.CatalogItem.Status == "available");
        Assert.DoesNotContain(entries, entry => entry.ModelId == Gemma12BLocalValidation.MtpDraftModelId);
        Assert.DoesNotContain(entries, entry => entry.ModelId == "ternary-bonsai-8b-q2-0");

        var catalog = new ModelCatalogService(paths).LoadBuiltInCatalog();
        Assert.Contains(catalog.Models, model =>
            model.ModelId == Gemma12BLocalValidation.ModelId &&
            model.Role == "review" &&
            model.Family == "gemma" &&
            model.Runtime.PackageId == "runtime-llama-cpp" &&
            model.SizeBytes == 6975877728 &&
            model.Requirements.GpuRequired &&
            model.RecommendedFor.Contains("gemma4_12b_mtp") &&
            model.Status == "available");
        Assert.Contains(catalog.Models, model =>
            model.ModelId == Gemma12BLocalValidation.MtpDraftModelId &&
            model.Role == "review_aux" &&
            model.Status == "hidden" &&
            model.Download.Url!.Contains("gemma-4-12B-it-qat-assistant-MTP-Q8_0.gguf", StringComparison.Ordinal));
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
        Assert.Equal("whisper-small", lightweight.AsrModelId);
        Assert.Equal("bonsai-8b-q1-0", lightweight.ReviewModelId);

        var recommended = presets.Single(preset => preset.PresetId == "recommended");
        Assert.Equal("faster-whisper-large-v3-turbo", recommended.AsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", recommended.ReviewModelId);

        var highAccuracy = presets.Single(preset => preset.PresetId == "high_accuracy");
        Assert.Equal("faster-whisper-large-v3", highAccuracy.AsrModelId);
        Assert.Equal(Gemma12BLocalValidation.ModelId, highAccuracy.ReviewModelId);

        var experimental = presets.Single(preset => preset.PresetId == "experimental");
        Assert.Equal("faster-whisper-large-v3-turbo", experimental.AsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", experimental.ReviewModelId);
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
    public void ListEntries_IncludesInstalledHiddenModelForManagement()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogService = new ModelCatalogService(paths);
        var repository = new InstalledModelRepository(paths);
        var installService = new ModelInstallService(paths, repository, new ModelVerificationService());
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "ternary-bonsai-8b-q2-0");
        var modelPath = installService.GetDefaultInstallPath(catalogItem);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "hidden model");

        installService.RegisterLocalModel(catalogItem, modelPath, "download");

        var entry = catalogService.ListEntries().Single(entry => entry.ModelId == catalogItem.ModelId);
        Assert.True(entry.IsInstalled);
        Assert.True(entry.IsVerified);
        Assert.Equal("hidden", entry.CatalogItem.Status);
    }

    [Fact]
    public void ListEntries_HidesHiddenModelWhenInstalledFileIsMissing()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogService = new ModelCatalogService(paths);
        var repository = new InstalledModelRepository(paths);
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "ternary-bonsai-8b-q2-0");
        repository.UpsertInstalledModel(new InstalledModel(
            catalogItem.ModelId,
            catalogItem.Role,
            catalogItem.EngineId,
            catalogItem.DisplayName,
            catalogItem.Family,
            Version: null,
            FilePath: Path.Combine(paths.UserModels, "review", "missing.gguf"),
            ManifestPath: null,
            SizeBytes: catalogItem.SizeBytes,
            Sha256: null,
            Verified: true,
            LicenseName: catalogItem.License.Name,
            SourceType: "download",
            InstalledAt: DateTimeOffset.Now,
            LastVerifiedAt: DateTimeOffset.Now,
            Status: "installed"));

        var entries = catalogService.ListEntries();

        Assert.DoesNotContain(entries, entry => entry.ModelId == catalogItem.ModelId);
    }

    [Fact]
    public void ListEntries_IncludesHiddenModelWithActiveDownloadJobForManagement()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogService = new ModelCatalogService(paths);
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "ternary-bonsai-8b-q2-0");
        var downloadJobs = new ModelDownloadJobRepository(paths);
        var downloadId = downloadJobs.Start(
            catalogItem.ModelId,
            catalogItem.Download.Url!,
            Path.Combine(paths.UserModels, "review", "ternary-bonsai-8b-q2-0.gguf"),
            Path.Combine(paths.UserModels, "review", "ternary-bonsai-8b-q2-0.gguf.partial"),
            catalogItem.Download.Sha256);
        downloadJobs.MarkPaused(downloadId);

        var entry = catalogService.ListEntries().Single(entry => entry.ModelId == catalogItem.ModelId);

        Assert.False(entry.IsInstalled);
        Assert.Equal("hidden", entry.CatalogItem.Status);
        Assert.Equal("paused", entry.DownloadState);
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
