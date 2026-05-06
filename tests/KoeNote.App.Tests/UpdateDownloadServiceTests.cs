using System.Net;
using System.Security.Cryptography;
using KoeNote.App.Services;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateDownloadServiceTests
{
    [Fact]
    public async Task DownloadAndVerifyAsync_SavesVerifiedInstaller()
    {
        var payload = "msi-bytes"u8.ToArray();
        var release = CreateRelease(payload);
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var service = new UpdateDownloadService(new HttpClient(new StaticBytesHandler(payload)), paths);

        var result = await service.DownloadAndVerifyAsync(release);

        Assert.True(File.Exists(result.FilePath));
        Assert.Equal(payload.Length, result.BytesDownloaded);
        Assert.Equal(release.Sha256, result.Sha256);
        Assert.Equal(Path.Combine(paths.UpdateDownloads, "KoeNote-v0.14.0-win-x64.msi"), result.FilePath);
        Assert.False(File.Exists(result.FilePath + ".download"));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_DeletesTempFileWhenSha256DoesNotMatch()
    {
        var payload = "tampered-msi-bytes"u8.ToArray();
        var release = new LatestReleaseInfo(
            "0.14.0",
            new Uri("https://example.test/releases/KoeNote-v0.14.0-win-x64.msi"),
            new string('a', 64),
            null,
            null,
            false,
            "win-x64",
            null);
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var service = new UpdateDownloadService(new HttpClient(new StaticBytesHandler(payload)), paths);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndVerifyAsync(release));

        Assert.False(File.Exists(Path.Combine(paths.UpdateDownloads, "KoeNote-v0.14.0-win-x64.msi")));
        Assert.False(File.Exists(Path.Combine(paths.UpdateDownloads, "KoeNote-v0.14.0-win-x64.msi.download")));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_ReportsProgress()
    {
        var payload = new byte[100_000];
        RandomNumberGenerator.Fill(payload);
        var release = CreateRelease(payload);
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var service = new UpdateDownloadService(new HttpClient(new StaticBytesHandler(payload)), paths);
        var progressItems = new List<UpdateDownloadProgress>();

        await service.DownloadAndVerifyAsync(release, new Progress<UpdateDownloadProgress>(progressItems.Add));

        Assert.NotEmpty(progressItems);
        Assert.Equal(payload.Length, progressItems[^1].BytesDownloaded);
        Assert.Equal(payload.Length, progressItems[^1].BytesTotal);
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_RejectsNonHttpsMsiUrl()
    {
        var payload = "msi-bytes"u8.ToArray();
        var release = CreateRelease(payload) with
        {
            MsiUrl = new Uri("http://example.test/releases/KoeNote-v0.14.0-win-x64.msi")
        };
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var service = new UpdateDownloadService(new HttpClient(new StaticBytesHandler(payload)), paths);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndVerifyAsync(release));
    }

    [Fact]
    public async Task DownloadAndVerifyAsync_DeletesTempFileWhenDownloadFails()
    {
        var payload = "partial-msi-bytes"u8.ToArray();
        var release = CreateRelease(payload);
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        var service = new UpdateDownloadService(new HttpClient(new FailingStreamHandler(payload)), paths);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAndVerifyAsync(release));

        Assert.False(File.Exists(Path.Combine(paths.UpdateDownloads, "KoeNote-v0.14.0-win-x64.msi")));
        Assert.False(File.Exists(Path.Combine(paths.UpdateDownloads, "KoeNote-v0.14.0-win-x64.msi.download")));
    }

    private static LatestReleaseInfo CreateRelease(byte[] payload)
    {
        return new LatestReleaseInfo(
            "0.14.0",
            new Uri("https://example.test/releases/KoeNote-v0.14.0-win-x64.msi"),
            Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(),
            null,
            new Uri("https://example.test/releases/0.14.0"),
            false,
            "win-x64",
            null);
    }

    private sealed class StaticBytesHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            response.Content.Headers.ContentLength = payload.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class FailingStreamHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingReadStream(payload))
            };
            response.Content.Headers.ContentLength = payload.Length;
            return Task.FromResult(response);
        }
    }

    private sealed class FailingReadStream(byte[] payload) : MemoryStream(payload)
    {
        private bool _hasRead;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_hasRead)
            {
                throw new HttpRequestException("Simulated interrupted download.");
            }

            _hasRead = true;
            return base.Read(buffer, offset, Math.Min(count, 4));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_hasRead)
            {
                throw new HttpRequestException("Simulated interrupted download.");
            }

            _hasRead = true;
            return base.ReadAsync(buffer[..Math.Min(buffer.Length, 4)], cancellationToken);
        }
    }
}
