using System.Net;
using System.Net.Http;
using KoeNote.App.Services;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;

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
    public async Task DownloadAndInstallAsync_RejectsHuggingFaceSearchPages()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null) with
        {
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/models?search=faster-whisper-large-v3", null)
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
    public async Task DownloadAndInstallAsync_ThrottlesProgressReportsForLargeFiles()
    {
        var paths = CreatePaths();
        var payload = new byte[20 * 1024 * 1024];
        var catalogItem = CreateCatalogItem(null);
        var targetPath = Path.Combine(paths.UserModels, "asr", "large.bin");
        var reports = new List<ModelDownloadProgress>();
        var service = CreateService(paths, new ByteArrayHandler(payload));

        await service.DownloadAndInstallAsync(catalogItem, targetPath, new SynchronousProgress<ModelDownloadProgress>(reports.Add));

        Assert.InRange(reports.Count, 1, 5);
        Assert.Equal(payload.Length, reports.Last().BytesDownloaded);
        Assert.Equal(payload.Length, reports.Last().BytesTotal);
        var latest = new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId);
        Assert.Equal(payload.Length, latest?.BytesDownloaded);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_SavesLatestProgressWhenCancelledBetweenThrottledReports()
    {
        var paths = CreatePaths();
        var totalBytes = 4 * 1024 * 1024;
        var cancelAfterBytes = 2 * 1024 * 1024;
        var catalogItem = CreateCatalogItem(null);
        var targetPath = Path.Combine(paths.UserModels, "asr", "cancelled-large.bin");
        var reports = new List<ModelDownloadProgress>();
        var service = CreateService(paths, new CancellingStreamHandler(totalBytes, cancelAfterBytes));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.DownloadAndInstallAsync(catalogItem, targetPath, new SynchronousProgress<ModelDownloadProgress>(reports.Add)));

        var latest = new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId);
        Assert.Equal("cancelled", latest?.Status);
        Assert.Equal(cancelAfterBytes, latest?.BytesDownloaded);
        Assert.Equal(cancelAfterBytes, reports.Last().BytesDownloaded);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_DownloadsHuggingFaceRepositoryToDirectory()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null) with
        {
            ModelId = "hf-repo-model",
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/org/repo-model", null)
        };
        var targetPath = Path.Combine(paths.UserModels, "asr", "repo-model");
        var service = CreateService(paths, new HuggingFaceRepositoryHandler());

        var installed = await service.DownloadAndInstallAsync(catalogItem, targetPath);

        Assert.True(installed.Verified);
        Assert.True(Directory.Exists(targetPath));
        Assert.Equal("model", File.ReadAllText(Path.Combine(targetPath, "model.bin")));
        Assert.Equal("config", File.ReadAllText(Path.Combine(targetPath, "nested", "config.json")));
    }

    [Fact]
    public async Task DownloadAndInstallAsync_UsesCatalogSizeWhenHuggingFaceFileSizesAreMissing()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null) with
        {
            ModelId = "hf-repo-model",
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/org/repo-model", null),
            SizeBytes = 11
        };
        var reports = new List<ModelDownloadProgress>();
        var targetPath = Path.Combine(paths.UserModels, "asr", "repo-model");
        var service = CreateService(paths, new HuggingFaceRepositoryHandler(includeSizes: false));

        await service.DownloadAndInstallAsync(catalogItem, targetPath, new Progress<ModelDownloadProgress>(reports.Add));

        Assert.Contains(reports, report => report.BytesDownloaded > 0 && report.BytesTotal == 11);
        var latest = new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId);
        Assert.Equal(11, latest?.BytesTotal);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_ReportsUnknownTotalWhenDownloadedBytesExceedCatalogSize()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null) with
        {
            ModelId = "hf-repo-model",
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/org/repo-model", null),
            SizeBytes = 2
        };
        var reports = new List<ModelDownloadProgress>();
        var targetPath = Path.Combine(paths.UserModels, "asr", "repo-model");
        var service = CreateService(paths, new HuggingFaceRepositoryHandler(includeSizes: false));

        await service.DownloadAndInstallAsync(catalogItem, targetPath, new Progress<ModelDownloadProgress>(reports.Add));

        Assert.Contains(reports, report => report.BytesDownloaded > 2 && report.BytesTotal is null);
    }

    [Fact]
    public async Task DownloadAndInstallAsync_ReplacesPartialHuggingFaceFilesWithoutDoubleCountingProgress()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null) with
        {
            ModelId = "hf-repo-model",
            Download = new ModelDownloadSpec("huggingface", "https://huggingface.co/org/repo-model", null)
        };
        var targetPath = Path.Combine(paths.UserModels, "asr", "repo-model");
        var partialPath = $"{targetPath}.partial";
        Directory.CreateDirectory(partialPath);
        File.WriteAllText(Path.Combine(partialPath, "model.bin"), "mo");
        var reports = new List<ModelDownloadProgress>();
        var service = CreateService(paths, new HuggingFaceRepositoryHandler());

        await service.DownloadAndInstallAsync(catalogItem, targetPath, new Progress<ModelDownloadProgress>(reports.Add));

        Assert.True(Directory.Exists(targetPath));
        Assert.DoesNotContain(reports, report => report.BytesTotal is { } total && report.BytesDownloaded > total);
        var latest = new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId);
        Assert.Equal(11, latest?.BytesDownloaded);
        Assert.Equal(11, latest?.BytesTotal);
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

    [Fact]
    public async Task ResumeDownloadAndInstallAsync_ReportsExistingProgressBeforeReadingNetwork()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null);
        var targetPath = Path.Combine(paths.UserModels, "asr", "progress.bin");
        var repository = new ModelDownloadJobRepository(paths);
        var downloadId = repository.Start(catalogItem.ModelId, catalogItem.Download.Url!, targetPath, $"{targetPath}.partial", null);
        repository.UpdateProgress(downloadId, 128, 1024);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var reports = new List<ModelDownloadProgress>();
        var service = CreateService(paths, new StubHandler("payload"));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ResumeDownloadAndInstallAsync(
                catalogItem,
                downloadId,
                new Progress<ModelDownloadProgress>(reports.Add),
                cancellation.Token));

        var report = Assert.Single(reports);
        Assert.Equal(128, report.BytesDownloaded);
        Assert.Equal(1024, report.BytesTotal);
    }

    [Fact]
    public async Task ResumeDownloadAndInstallAsync_PreservesPausedStatusWhenCancelledAfterPause()
    {
        var paths = CreatePaths();
        var catalogItem = CreateCatalogItem(null);
        var targetPath = Path.Combine(paths.UserModels, "asr", "paused.bin");
        var repository = new ModelDownloadJobRepository(paths);
        var downloadId = repository.Start(catalogItem.ModelId, catalogItem.Download.Url!, targetPath, $"{targetPath}.partial", null);
        repository.MarkPaused(downloadId);
        var service = CreateService(paths, new StubHandler("payload"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ResumeDownloadAndInstallAsync(catalogItem, downloadId, cancellationToken: cancellation.Token));
        Assert.Equal("paused", repository.Find(downloadId)?.Status);
    }

    [Fact]
    public async Task SetupDownloadSelectedModelAsync_SkipsAlreadyInstalledModel()
    {
        var paths = CreatePaths();
        var catalogService = new ModelCatalogService(paths);
        var catalogItem = catalogService.LoadBuiltInCatalog().Models.First(model => model.ModelId == "faster-whisper-large-v3");
        var modelPath = Path.Combine(paths.UserModels, "asr", "faster-whisper-large-v3");
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(Path.Combine(modelPath, "model.bin"), "installed");
        var installedRepository = new InstalledModelRepository(paths);
        var verification = new ModelVerificationService();
        var installService = new ModelInstallService(paths, installedRepository, verification);
        installService.RegisterLocalModel(catalogItem, modelPath, "download");
        var stateService = new SetupStateService(paths);
        stateService.Save(SetupState.Default(paths.UserModels) with
        {
            SelectedAsrModelId = catalogItem.ModelId
        });
        var setup = new SetupWizardService(
            paths,
            stateService,
            new ToolStatusService(paths),
            catalogService,
            installedRepository,
            installService,
            new ModelPackImportService(paths, catalogService, installService),
            new ModelDownloadService(new HttpClient(new ThrowingHandler()), new ModelDownloadJobRepository(paths), verification, installService),
            new DiarizationRuntimeService(paths, new ExternalProcessRunner()));

        var result = await setup.DownloadSelectedModelAsync("asr");

        Assert.True(result.IsSucceeded);
        Assert.Contains("Already installed", result.Message, StringComparison.Ordinal);
        Assert.Null(new ModelDownloadJobRepository(paths).FindLatestForModel(catalogItem.ModelId));
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

    private sealed class ByteArrayHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            });
        }
    }

    private sealed class CancellingStreamHandler(long totalBytes, long cancelAfterBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new CancellingReadStream(totalBytes, cancelAfterBytes));
            content.Headers.ContentLength = totalBytes;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }

    private sealed class CancellingReadStream(long totalBytes, long cancelAfterBytes) : Stream
    {
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => totalBytes;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= cancelAfterBytes)
            {
                throw new OperationCanceledException();
            }

            var remainingBeforeCancel = cancelAfterBytes - _position;
            var read = (int)Math.Min(count, remainingBeforeCancel);
            Array.Clear(buffer, offset, read);
            _position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= cancelAfterBytes)
            {
                throw new OperationCanceledException();
            }

            var remainingBeforeCancel = cancelAfterBytes - _position;
            var read = (int)Math.Min(buffer.Length, remainingBeforeCancel);
            buffer.Span[..read].Clear();
            _position += read;
            return ValueTask.FromResult(read);
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value)
        {
            handler(value);
        }
    }

    private sealed class HuggingFaceRepositoryHandler(bool includeSizes = true) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            var metadata = includeSizes
                ? """
                    {
                      "siblings": [
                        { "rfilename": ".gitattributes", "size": 1 },
                        { "rfilename": "model.bin", "size": 5 },
                        { "rfilename": "nested/config.json", "size": 6 }
                      ]
                    }
                    """
                : """
                    {
                      "siblings": [
                        { "rfilename": ".gitattributes" },
                        { "rfilename": "model.bin" },
                        { "rfilename": "nested/config.json" }
                      ]
                    }
                    """;
            var body = path switch
            {
                "/api/models/org/repo-model" => metadata,
                "/org/repo-model/resolve/main/model.bin" => "model",
                "/org/repo-model/resolve/main/nested/config.json" => "config",
                _ => throw new InvalidOperationException($"Unexpected URL: {request.RequestUri}")
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
        }
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Network should not be used for installed models.");
        }
    }

}
