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
    public void OptionsFromEnvironment_UsesNvidiaRedistDefaultsWhenOverridesAreUnset()
    {
        var originalManifest = Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RedistManifestUrlEnvironmentVariable);
        var originalBase = Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RedistBaseUrlEnvironmentVariable);
        var originalLegacyUrl = Environment.GetEnvironmentVariable(CudaReviewRuntimeService.RuntimeUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(CudaReviewRuntimeService.RedistManifestUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CudaReviewRuntimeService.RedistBaseUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(CudaReviewRuntimeService.RuntimeUrlEnvironmentVariable, null);

            var options = CudaReviewRuntimeOptions.FromEnvironment();

            Assert.Equal(CudaReviewRuntimeService.DefaultRedistManifestUrl, options.RedistManifestUrl);
            Assert.Equal(CudaReviewRuntimeService.DefaultRedistBaseUrl, options.RedistBaseUrl);
            Assert.Null(options.LegacyRuntimeUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CudaReviewRuntimeService.RedistManifestUrlEnvironmentVariable, originalManifest);
            Environment.SetEnvironmentVariable(CudaReviewRuntimeService.RedistBaseUrlEnvironmentVariable, originalBase);
            Environment.SetEnvironmentVariable(CudaReviewRuntimeService.RuntimeUrlEnvironmentVariable, originalLegacyUrl);
        }
    }

    [Fact]
    public async Task InstallAsync_DownloadsNvidiaRedistDllsWhenGpuBridgeIsBundled()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "koenote bridge");
        var cudartArchive = CreateArchive(("cuda/bin/cudart64_12.dll", "cudart"));
        var cublasArchive = CreateArchive(
            ("lib/bin/cublas64_12.dll", "cublas"),
            ("lib/bin/cublasLt64_12.dll", "cublasLt"),
            ("lib/bin/readme.txt", "ignored"));
        var manifest = CreateManifest(cudartArchive, cublasArchive);
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new MapHandler(new Dictionary<string, byte[]>
            {
                ["https://example.test/redist.json"] = manifest,
                ["https://example.test/redist/cuda-cudart.zip"] = cudartArchive,
                ["https://example.test/redist/libcublas.zip"] = cublasArchive
            })),
            new CudaReviewRuntimeOptions("https://example.test/redist.json", "https://example.test/redist/"));
        var progress = new ListProgress();

        var result = await service.InstallAsync(progress: progress);

        Assert.True(result.IsSucceeded);
        Assert.Contains(progress.Items, item => item.StageText == "ダウンロード中" && item.Message.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progress.Items, item => item.StageText == "検証中" && item.Message.Contains("sha256", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progress.Items, item => item.StageText == "展開中");
        Assert.Contains(progress.Items, item => item.StageText == "インストール中");
        Assert.True(CudaReviewRuntimeLayout.HasPackage(paths));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cudart64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cublas64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cublasLt64_12.dll")));
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "readme.txt")));
        Assert.True(File.Exists(paths.CudaReviewRuntimeMarkerPath));
    }

    [Fact]
    public async Task InstallAsync_ReusesLocalCudaToolkitDllsBeforeDownloading()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "koenote bridge");
        var localCudaBin = Path.Combine(root, "cuda", "bin");
        Directory.CreateDirectory(localCudaBin);
        File.WriteAllText(Path.Combine(localCudaBin, "cudart64_12.dll"), "local cudart");
        File.WriteAllText(Path.Combine(localCudaBin, "cublas64_12.dll"), "local cublas");
        File.WriteAllText(Path.Combine(localCudaBin, "cublasLt64_12.dll"), "local cublasLt");
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new FailingHandler()),
            new CudaReviewRuntimeOptions(
                "https://example.test/redist.json",
                "https://example.test/redist/",
                LocalSearchRoots: [localCudaBin]));

        var result = await service.InstallAsync();

        Assert.True(result.IsSucceeded);
        Assert.True(CudaReviewRuntimeLayout.HasPackage(paths));
        Assert.Equal("local cudart", File.ReadAllText(Path.Combine(paths.ReviewRuntimeDirectory, "cudart64_12.dll")));
    }

    [Fact]
    public async Task InstallAsync_RequiresExistingCpuReviewRuntime()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.ReviewRuntimeDirectory);
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "koenote bridge");
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new FailingHandler()),
            new CudaReviewRuntimeOptions("https://example.test/redist.json", "https://example.test/redist/"));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryCpuRuntimeMissing, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cudart64_12.dll")));
    }

    [Fact]
    public async Task InstallAsync_RequiresBundledGpuBridgeBeforeDownloadingNvidiaRedist()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new FailingHandler()),
            new CudaReviewRuntimeOptions("https://example.test/redist.json", "https://example.test/redist/"));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryBundledRuntimeMissing, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cudart64_12.dll")));
    }

    [Fact]
    public async Task InstallAsync_RejectsNvidiaRedistHashMismatch()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "koenote bridge");
        var cudartArchive = CreateArchive(("cuda/bin/cudart64_12.dll", "cudart"));
        var cublasArchive = CreateArchive(
            ("lib/bin/cublas64_12.dll", "cublas"),
            ("lib/bin/cublasLt64_12.dll", "cublasLt"));
        var manifest = CreateManifest(cudartArchive, cublasArchive, corruptCudartHash: true);
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new MapHandler(new Dictionary<string, byte[]>
            {
                ["https://example.test/redist.json"] = manifest,
                ["https://example.test/redist/cuda-cudart.zip"] = cudartArchive,
                ["https://example.test/redist/libcublas.zip"] = cublasArchive
            })),
            new CudaReviewRuntimeOptions("https://example.test/redist.json", "https://example.test/redist/"));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(CudaReviewRuntimeService.FailureCategoryHashMismatch, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "cudart64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
    }

    [Fact]
    public async Task InstallAsync_KeepsLegacyAllInOneZipAsExplicitFallback()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        var archive = CreateArchive(
            ("bin/ggml-cuda.dll", "cuda"),
            ("bin/cudart64_12.dll", "cudart"),
            ("bin/cublas64_12.dll", "cublas"),
            ("bin/cublasLt64_12.dll", "cublasLt"));
        var expectedSha256 = ComputeSha256(archive);
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new MapHandler(new Dictionary<string, byte[]>
            {
                ["https://example.test/cuda-runtime.zip"] = archive
            })),
            new CudaReviewRuntimeOptions(
                "https://example.test/redist.json",
                "https://example.test/redist/",
                LegacyRuntimeUrl: "https://example.test/cuda-runtime.zip",
                LegacyRuntimeSha256: expectedSha256));

        var result = await service.InstallAsync();

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedSha256, result.Sha256);
        Assert.True(CudaReviewRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public async Task InstallAsync_ReturnsConfigurationFailureWhenRedistSourceIsMissing()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithCpuRuntime(root);
        File.WriteAllText(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"), "koenote bridge");
        var service = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new FailingHandler()),
            new CudaReviewRuntimeOptions(string.Empty, string.Empty));

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

    private static byte[] CreateManifest(byte[] cudartArchive, byte[] cublasArchive, bool corruptCudartHash = false)
    {
        var cudartSha = corruptCudartHash ? new string('0', 64) : ComputeSha256(cudartArchive);
        var cublasSha = ComputeSha256(cublasArchive);
        return System.Text.Encoding.UTF8.GetBytes(
            $$"""
            {
              "cuda_cudart": {
                "windows-x86_64": {
                  "relative_path": "cuda-cudart.zip",
                  "sha256": "{{cudartSha}}",
                  "size": {{cudartArchive.Length}}
                }
              },
              "libcublas": {
                "windows-x86_64": {
                  "relative_path": "libcublas.zip",
                  "sha256": "{{cublasSha}}",
                  "size": {{cublasArchive.Length}}
                }
              }
            }
            """);
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

    private sealed class MapHandler(IReadOnlyDictionary<string, byte[]> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null && responses.TryGetValue(request.RequestUri.ToString(), out var content))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(content)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException($"Unexpected HTTP request: {request.RequestUri}");
        }
    }

    private sealed class ListProgress : IProgress<RuntimeInstallProgress>
    {
        public List<RuntimeInstallProgress> Items { get; } = [];

        public void Report(RuntimeInstallProgress value)
        {
            Items.Add(value);
        }
    }
}
