using System.Net;
using System.Net.Http;
using KoeNote.App.Services;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class ModelDownloadServiceTests
{
    [Fact]
    public async Task DownloadAndInstallAsync_DownloadsVerifiesAndRegistersModel()
    {
        var paths = CreatePaths();
        var payload = "downloaded model";
        var sha256 = WriteAndHash(payload);
        var catalogItem = CreateCatalogItem(sha256);
        var service = CreateService(paths, new StubHandler(payload));
        var targetPath = Path.Combine(paths.UserModels, "asr", "downloaded.bin");

        var installed = await service.DownloadAndInstallAsync(catalogItem, targetPath);

        Assert.True(File.Exists(targetPath));
        Assert.Equal("download", installed.SourceType);
        Assert.True(installed.Verified);
        Assert.Equal("installed", installed.Status);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_RecordsFailureWithoutInstallingOnChecksumMismatch()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(new string('0', 64));
        var service = CreateService(paths, new StubHandler("bad payload"));
        var targetPath = Path.Combine(paths.UserModels, "asr", "bad.bin");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndInstallAsync(catalogItem, targetPath));

        var latest = new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId);
        Assert.Equal("failed", latest?.Status);
        Assert.False(File.Exists(targetPath));
        Assert.Null(new InstalledModelRepository(paths).FindInstalledModel(catalogItem.ModelId));
    }

    [Fact]
    public async Task DownloadAndInstallAsync_RejectsCatalogLandingPages()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null) with
        {
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/Systran/faster-whisper-large-v3", null)
        };
        var service = CreateService(paths, new StubHandler("<html>not a model</html>"));
        var targetPath = Path.Combine(paths.UserModels, "asr", "landing-page.bin");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndInstallAsync(catalogItem, targetPath));

        Assert.False(File.Exists(targetPath));
        Assert.Null(new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId));
    }

    [Fact]
    public async Task DownloadAndInstallAsync_AllowsHuggingFaceResolveUrls()
    {
        var paths = CreatePaths();
        var payload = "downloaded model";
        var sha256 = WriteAndHash(payload);
        var catalogItem = CreateCatalogItem(sha256) with
        {
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/org/model/resolve/main/model.bin", sha256)
        };
        var service = CreateService(paths, new StubHandler(payload));
        var targetPath = Path.Combine(paths.UserModels, "asr", "hf-resolve.bin");

        var installed = await service.DownloadAndInstallAsync(catalogItem, targetPath);

        Assert.True(installed.Verified);
        Assert.True(File.Exists(targetPath));
    }

    [Fact]
    public async Task ResumeDownloadAndInstallAsync_ContinuesPartialFile()
    {
        var paths = CreatePaths();
        var payload = "downloaded model";
        var sha256 = WriteAndHash(payload);
        var catalogItem = CreateCatalogItem(sha256);
        var targetPath = Path.Combine(paths.UserModels, "asr", "resume.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        File.WriteAllText($"{targetPath}.partial", "downloaded ");
        var repository = new ModelDownloadJobRepository(paths);
        var downloadId = repository.Start(catalogItem.ModelId, catalogItem.Download.Url!, targetPath, $"{targetPath}.partial", sha256);
        repository.UpdateProgress(downloadId, 11, payload.Length);
        repository.MarkPaused(downloadId);
        var service = CreateService(paths, new StubHandler("model", HttpStatusCode.PartialContent, payload.Length));

        var installed = await service.ResumeDownloadAndInstallAsync(catalogItem, downloadId);

        Assert.True(installed.Verified);
        Assert.Equal(payload, File.ReadAllText(targetPath));
    }

    private static AppPaths CreatePaths()
    {
        var paths = new AppPaths(CreateRoot(), CreateRoot(), AppContext.BaseDirectory);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }

    private static ModelDownloadService CreateService(AppPaths paths, HttpMessageHandler handler)
    {
        var repository = new ModelDownloadJobRepository(paths);
        var verification = new ModelVerificationService();
        var install = new ModelInstallService(paths, new InstalledModelRepository(paths), verification);
        return new ModelDownloadService(new HttpClient(handler), repository, verification, install);
    }

    private static ModelCatalogItem CreateCatalogItem(string? sha256)
    {
        return new ModelCatalogItem(
            "download-model",
            "test",
            "asr",
            "vibevoice-crispasr",
            "Download Model",
            ["ja"],
            [],
            new ModelRuntimeSpec("test-runtime", "runtime-test"),
            new ModelDownloadSpec("https", "https://example.com/model.bin", sha256),
            new ModelLicenseSpec("Test license", null),
            new ModelRequirements(false, 0, false),
            "available");
    }

    private static string WriteAndHash(string text)
    {
        var path = Path.Combine(CreateRoot(), "hash.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return ModelVerificationService.ComputeSha256(path);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private sealed class StubHandler(string body, HttpStatusCode statusCode = HttpStatusCode.OK, long? contentRangeLength = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body)
            };

            if (contentRangeLength is { } length)
            {
                response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, length - 1, length);
            }

            return Task.FromResult(response);
        }
    }
}
