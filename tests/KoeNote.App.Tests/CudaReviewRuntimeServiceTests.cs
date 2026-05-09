using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using KoeNote.App.Services;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class CudaReviewRuntimeServiceTests
{
    [Fact]
    public async Task InstallAsync_DownloadsAndInstallsCudaRuntimeFiles()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        var archive = CreateArchive(
            ("llama-completion.exe", "ignored"),
            ("bin/ggml-cuda.dll", "cuda"),
            ("bin/cublas64_12.dll", "cublas"),
            ("bin/readme.txt", "ignored"));
        var expectedSha256 = ComputeSha256(archive);
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new CudaReviewRuntimeOptions("https://example.test/cuda-runtime.zip", expectedSha256));

        var result = await service.InstallAsync();

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedSha256, result.Sha256);
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cublas64_12.dll")));
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "readme.txt")));
        Assert.True(File.Exists(paths.CudaReviewRuntimeMarkerPath));
        Assert.True(File.Exists(paths.LlamaCompletionPath));
    }

    [Fact]
    public async Task InstallAsync_RequiresExistingCpuReviewRuntime()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(("ggml-cuda.dll", "cuda"));
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new CudaReviewRuntimeOptions("https://example.test/cuda-runtime.zip", null));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryCpuRuntimeMissing, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
    }

    [Fact]
    public async Task InstallAsync_RejectsHashMismatch()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        var archive = CreateArchive(("ggml-cuda.dll", "cuda"));
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new CudaReviewRuntimeOptions("https://example.test/cuda-runtime.zip", new string('0', 64)));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryHashMismatch, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
        Assert.True(File.Exists(paths.LlamaCompletionPath));
    }

    [Fact]
    public async Task InstallAsync_RejectsArchiveWithoutCudaRuntime()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        var archive = CreateArchive(("llama-completion.exe", "runtime"), ("ggml.dll", "cpu"));
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new CudaReviewRuntimeOptions("https://example.test/cuda-runtime.zip", null));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryArchiveInvalid, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
        Assert.True(File.Exists(paths.LlamaCompletionPath));
    }

    [Fact]
    public async Task InstallAsync_ReturnsConfigurationFailureWhenUrlIsMissing()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler([])),
            new CudaReviewRuntimeOptions(string.Empty, null));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryConfigurationMissing, result.FailureCategory);
    }

    private static AppPaths CreatePathsWithCpuRuntime(string root)
    {
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
        File.WriteAllText(paths.LlamaCompletionPath, "cpu runtime");
        return paths;
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
