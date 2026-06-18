using System.IO.Compression;
using System.Net;
using System.Net.Http;
using KoeNote.App.Services;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Tests;

public sealed class TernaryReviewRuntimeServiceTests
{
    [Fact]
    public async Task InstallAsync_DownloadsAndExtractsRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(("llama-completion.exe", "runtime"), ("ggml.dll", "dll"));
        var service = new TernaryReviewRuntimeService(paths, new HttpClient(new ArchiveHandler(archive)));
        var progress = new ListProgress();

        var result = await service.InstallAsync(progress: progress);

        Assert.True(result.IsSucceeded);
        Assert.Contains(progress.Items, item =>
            item.BytesTotal == archive.Length &&
            item.BytesDownloaded == archive.Length &&
            item.DisplayPercent == 100);
        Assert.True(File.Exists(paths.TernaryLlamaCompletionPath));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(paths.TernaryLlamaCompletionPath)!, "ggml.dll")));
    }

    [Fact]
    public async Task InstallAsync_RejectsArchiveWithoutCompletionRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        paths.EnsureCreated();
        var archive = CreateArchive(("ggml.dll", "dll"));
        var service = new TernaryReviewRuntimeService(paths, new HttpClient(new ArchiveHandler(archive)));

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(TernaryReviewRuntimeService.FailureCategoryArchiveInvalid, result.FailureCategory);
        Assert.False(File.Exists(paths.TernaryLlamaCompletionPath));
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

    private sealed class ListProgress : IProgress<RuntimeInstallProgress>
    {
        public List<RuntimeInstallProgress> Items { get; } = [];

        public void Report(RuntimeInstallProgress value)
        {
            Items.Add(value);
        }
    }
}
