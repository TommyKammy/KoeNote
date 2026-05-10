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
    public void OptionsFromEnvironment_UsesNvidiaRedistDefaultsWhenOverridesAreUnset()
    {
        var originalCudaManifest = Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistManifestUrlEnvironmentVariable);
        var originalCudaBase = Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistBaseUrlEnvironmentVariable);
        var originalCudnnManifest = Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistManifestUrlEnvironmentVariable);
        var originalCudnnBase = Environment.GetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistBaseUrlEnvironmentVariable);
        var originalLegacyUrl = Environment.GetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistManifestUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistBaseUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistManifestUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistBaseUrlEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable, null);

            var options = AsrCudaRuntimeOptions.FromEnvironment();

            Assert.Equal(AsrCudaRuntimeService.DefaultCudaRedistManifestUrl, options.CudaRedistManifestUrl);
            Assert.Equal(AsrCudaRuntimeService.DefaultCudaRedistBaseUrl, options.CudaRedistBaseUrl);
            Assert.Equal(AsrCudaRuntimeService.DefaultCudnnRedistManifestUrl, options.CudnnRedistManifestUrl);
            Assert.Equal(AsrCudaRuntimeService.DefaultCudnnRedistBaseUrl, options.CudnnRedistBaseUrl);
            Assert.Null(options.LegacyRuntimeUrl);
        }
        finally
        {
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistManifestUrlEnvironmentVariable, originalCudaManifest);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudaRedistBaseUrlEnvironmentVariable, originalCudaBase);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistManifestUrlEnvironmentVariable, originalCudnnManifest);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.CudnnRedistBaseUrlEnvironmentVariable, originalCudnnBase);
            Environment.SetEnvironmentVariable(AsrCudaRuntimeService.RuntimeUrlEnvironmentVariable, originalLegacyUrl);
        }
    }

    [Fact]
    public async Task InstallAsync_DownloadsNvidiaRedistDllsWhenAsrGpuFilesAreBundled()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithBundledAsrRuntime(root);
        var cudartArchive = CreateArchive(("cuda/bin/cudart64_12.dll", "cudart"));
        var cublasArchive = CreateArchive(
            ("lib/bin/cublas64_12.dll", "cublas"),
            ("lib/bin/cublasLt64_12.dll", "cublasLt"));
        var cudnnArchive = CreateArchive(
            ("cudnn/bin/cudnn64_9.dll", "cudnn"),
            ("cudnn/bin/readme.txt", "ignored"));
        var cudaManifest = CreateCudaManifest(cudartArchive, cublasArchive);
        var cudnnManifest = CreateCudnnManifest(cudnnArchive);
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new MapHandler(new Dictionary<string, byte[]>
            {
                ["https://example.test/cuda.json"] = cudaManifest,
                ["https://example.test/cuda/cuda-cudart.zip"] = cudartArchive,
                ["https://example.test/cuda/libcublas.zip"] = cublasArchive,
                ["https://example.test/cudnn.json"] = cudnnManifest,
                ["https://example.test/cudnn/cudnn.zip"] = cudnnArchive
            })),
            CreateOptions());
        var progress = new ListProgress();

        var result = await service.InstallAsync(progress: progress);

        Assert.True(result.IsSucceeded);
        Assert.Contains(progress.Items, item => item.StageText == "ダウンロード中" && item.Message.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progress.Items, item => item.StageText == "検証中" && item.Message.Contains("sha256", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(progress.Items, item => item.StageText == "展開中");
        Assert.Contains(progress.Items, item => item.StageText == "インストール中");
        Assert.True(AsrCudaRuntimeLayout.HasPackage(paths));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cublas64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cublasLt64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cudart64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cudnn64_9.dll")));
        Assert.False(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "readme.txt")));
        Assert.True(File.Exists(paths.AsrCudaRuntimeMarkerPath));
    }

    [Fact]
    public async Task InstallAsync_ReusesLocalCudaAndCudnnDllsBeforeDownloading()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithBundledAsrRuntime(root);
        var cudaBin = Path.Combine(root, "cuda", "bin");
        var cudnnBin = Path.Combine(root, "cudnn", "bin");
        Directory.CreateDirectory(cudaBin);
        Directory.CreateDirectory(cudnnBin);
        File.WriteAllText(Path.Combine(cudaBin, "cudart64_12.dll"), "local cudart");
        File.WriteAllText(Path.Combine(cudaBin, "cublas64_12.dll"), "local cublas");
        File.WriteAllText(Path.Combine(cudaBin, "cublasLt64_12.dll"), "local cublasLt");
        File.WriteAllText(Path.Combine(cudnnBin, "cudnn64_9.dll"), "local cudnn");
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new FailingHandler()),
            CreateOptions(LocalSearchRoots: [cudaBin, cudnnBin]));

        var result = await service.InstallAsync();

        Assert.True(result.IsSucceeded);
        Assert.True(AsrCudaRuntimeLayout.HasPackage(paths));
        Assert.Equal("local cudnn", File.ReadAllText(Path.Combine(paths.AsrRuntimeDirectory, "cudnn64_9.dll")));
    }

    [Fact]
    public async Task InstallAsync_RequiresBundledAsrGpuFilesBeforeDownloading()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var service = new AsrCudaRuntimeService(paths, new HttpClient(new FailingHandler()), CreateOptions());

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(AsrCudaRuntimeService.FailureCategoryBundledRuntimeMissing, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cudart64_12.dll")));
    }

    [Fact]
    public async Task InstallAsync_RejectsNvidiaRedistHashMismatch()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithBundledAsrRuntime(root);
        var cudartArchive = CreateArchive(("cuda/bin/cudart64_12.dll", "cudart"));
        var cublasArchive = CreateArchive(
            ("lib/bin/cublas64_12.dll", "cublas"),
            ("lib/bin/cublasLt64_12.dll", "cublasLt"));
        var cudnnArchive = CreateArchive(("cudnn/bin/cudnn64_9.dll", "cudnn"));
        var cudaManifest = CreateCudaManifest(cudartArchive, cublasArchive, corruptCublasHash: true);
        var cudnnManifest = CreateCudnnManifest(cudnnArchive);
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new MapHandler(new Dictionary<string, byte[]>
            {
                ["https://example.test/cuda.json"] = cudaManifest,
                ["https://example.test/cuda/cuda-cudart.zip"] = cudartArchive,
                ["https://example.test/cuda/libcublas.zip"] = cublasArchive,
                ["https://example.test/cudnn.json"] = cudnnManifest,
                ["https://example.test/cudnn/cudnn.zip"] = cudnnArchive
            })),
            CreateOptions());

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(AsrCudaRuntimeService.FailureCategoryHashMismatch, result.FailureCategory);
        Assert.False(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "cublas64_12.dll")));
        Assert.True(File.Exists(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.exe")));
    }

    [Fact]
    public async Task InstallAsync_KeepsLegacyAllInOneZipAsExplicitFallback()
    {
        var root = CreateRoot();
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(
            ("bin/crispasr.exe", "crisp exe"),
            ("bin/crispasr.dll", "crisp dll"),
            ("bin/whisper.dll", "whisper"),
            ("bin/ggml-cuda.dll", "ggml cuda"),
            ("bin/cublas64_12.dll", "cublas"),
            ("bin/cublasLt64_12.dll", "cublasLt"),
            ("bin/cudart64_12.dll", "cudart"),
            ("bin/cudnn64_9.dll", "cudnn"));
        var expectedSha256 = ComputeSha256(archive);
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new MapHandler(new Dictionary<string, byte[]>
            {
                ["https://example.test/cuda-asr-runtime.zip"] = archive
            })),
            CreateOptions(
                LegacyRuntimeUrl: "https://example.test/cuda-asr-runtime.zip",
                LegacyRuntimeSha256: expectedSha256));

        var result = await service.InstallAsync();

        Assert.True(result.IsSucceeded);
        Assert.Equal(expectedSha256, result.Sha256);
        Assert.True(AsrCudaRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public async Task InstallAsync_ReturnsConfigurationFailureWhenRedistSourceIsMissing()
    {
        var root = CreateRoot();
        var paths = CreatePathsWithBundledAsrRuntime(root);
        var service = new AsrCudaRuntimeService(
            paths,
            new HttpClient(new FailingHandler()),
            new AsrCudaRuntimeOptions(string.Empty, string.Empty, string.Empty, string.Empty));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(AsrCudaRuntimeService.FailureCategoryConfigurationMissing, result.FailureCategory);
    }

    private static AppPaths CreatePathsWithBundledAsrRuntime(string root)
    {
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.AsrRuntimeDirectory);
        File.WriteAllText(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.exe"), "crisp exe");
        File.WriteAllText(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.dll"), "crisp dll");
        File.WriteAllText(Path.Combine(paths.AsrRuntimeDirectory, "whisper.dll"), "whisper");
        File.WriteAllText(Path.Combine(paths.AsrRuntimeDirectory, "ggml-cuda.dll"), "ggml cuda");
        return paths;
    }

    private static AsrCudaRuntimeOptions CreateOptions(
        string? LegacyRuntimeUrl = null,
        string? LegacyRuntimeSha256 = null,
        IReadOnlyList<string>? LocalSearchRoots = null)
    {
        return new AsrCudaRuntimeOptions(
            "https://example.test/cuda.json",
            "https://example.test/cuda/",
            "https://example.test/cudnn.json",
            "https://example.test/cudnn/",
            LegacyRuntimeUrl,
            LegacyRuntimeSha256,
            LocalSearchRoots);
    }

    private static string CreateRoot()
    {
        return Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
    }

    private static byte[] CreateCudaManifest(byte[] cudartArchive, byte[] cublasArchive, bool corruptCublasHash = false)
    {
        var cudartSha = ComputeSha256(cudartArchive);
        var cublasSha = corruptCublasHash ? new string('0', 64) : ComputeSha256(cublasArchive);
        return System.Text.Encoding.UTF8.GetBytes(
            $$"""
            {
              "cuda_cudart": {
                "windows-x86_64": {
                  "relative_path": "cuda-cudart.zip",
                  "sha256": "{{cudartSha}}"
                }
              },
              "libcublas": {
                "windows-x86_64": {
                  "relative_path": "libcublas.zip",
                  "sha256": "{{cublasSha}}"
                }
              }
            }
            """);
    }

    private static byte[] CreateCudnnManifest(byte[] cudnnArchive)
    {
        var cudnnSha = ComputeSha256(cudnnArchive);
        return System.Text.Encoding.UTF8.GetBytes(
            $$"""
            {
              "cudnn": {
                "windows-x86_64": {
                  "cuda12": {
                    "relative_path": "cudnn.zip",
                    "sha256": "{{cudnnSha}}"
                  }
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
