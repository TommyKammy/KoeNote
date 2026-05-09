using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrCudaRuntimeServiceTests
{
    [Fact]
    public void OptionsFromEnvironment_UsesDefaultReleaseAssetWhenUrlIsUnset()
    {
        var originalUrl = Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable, null);

            var options = AsrCudaRuntimeOptions.FromEnvironment();

            Assert.Equal(AsrCudaRuntimeService.DefaultRuntimeUrl, options.RuntimeUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable, originalUrl);
        }
    }

    [Fact]
    public async Task InstallAsync_DownloadsAndInstallsCudaRuntimeFiles()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(
            ("bin/cublas64_12.dll", "cublas"),
            ("bin/cublasLt64_12.dll", "cublasLt"),
            ("bin/cudart64_12.dll", "cudart"),
            ("bin/cudnn64_9.dll", "cudnn"),
            ("bin/zlibwapi.dll", "zlib"),
            ("bin/readme.txt", "ignored"));
        var expectedSha256 = ComputeSha256(archive);
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new AsrCudaRuntimeOptions("https://example.test/cuda-asr-runtime.zip", expectedSha256));

        var result = await service.InstallAsync();

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedSha256, result.Sha256);
        Assert.True(AsrCudaRuntimeLayout.HasPackage(paths));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cublas64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cublasLt64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cudart64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cudnn64_9.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "zlibwapi.dll")));
        Assert.False(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "readme.txt")));
        Assert.True(File.Exists(paths.AsrCudaRuntimeMarkerPath));
    }

    [Fact]
    public async Task InstallAsync_RejectsArchiveWithoutCublasAndCudnn()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(("bin/cudart64_12.dll", "runtime"));
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new AsrCudaRuntimeOptions("https://example.test/cuda-asr-runtime.zip", null));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(AsrCudaRuntimeService.FailureCategoryArchiveInvalid, result.FailureCategory);
        Assert.False(AsrCudaRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public async Task InstallAsync_RejectsArchiveMissingCudaRuntimeDependencies()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(
            ("bin/cublas64_12.dll", "cublas"),
            ("bin/cudnn64_9.dll", "cudnn"),
            ("bin/zlibwapi.dll", "zlib"));
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new AsrCudaRuntimeOptions("https://example.test/cuda-asr-runtime.zip", null));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(AsrCudaRuntimeService.FailureCategoryArchiveInvalid, result.FailureCategory);
        Assert.False(AsrCudaRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public async Task InstallAsync_ReturnsConfigurationFailureWhenUrlIsMissing()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler([])),
            new AsrCudaRuntimeOptions(string.Empty, null));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(AsrCudaRuntimeService.FailureCategoryConfigurationMissing, result.FailureCategory);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private static byte[] CreateArchive(params (string Path, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            });
        }
    }
}
