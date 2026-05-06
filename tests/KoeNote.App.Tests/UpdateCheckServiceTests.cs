using System.Net;
using KoeNote.App.Services.Updates;

namespace KoeNote.App.Tests;

public sealed class UpdateCheckServiceTests
{
    [Fact]
    public async Task CheckAsync_ReturnsAvailableUpdateForNewerLatestJson()
    {
        var service = CreateService("""
            {
              "schema_version": 1,
              "version": "0.14.0",
              "runtime_identifier": "win-x64",
              "msi_url": "https://example.test/downloads/KoeNote-v0.14.0-win-x64.msi",
              "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
              "sha256_url": "https://example.test/downloads/KoeNote-v0.14.0-win-x64.msi.sha256",
              "release_notes_url": "https://example.test/releases/0.14.0",
              "mandatory": true
            }
            """);

        var result = await service.CheckAsync();

        Assert.True(result.IsConfigured);
        Assert.True(result.IsUpdateAvailable);
        Assert.True(result.IsMandatory);
        Assert.Equal("0.14.0", result.LatestRelease?.Version);
        Assert.Equal("https://example.test/releases/0.14.0", result.LatestRelease?.ReleaseNotesUrl?.AbsoluteUri);
    }

    [Fact]
    public async Task CheckAsync_ReturnsUpToDateWhenLatestIsCurrentVersion()
    {
        var service = CreateService("""
            {
              "schema_version": 1,
              "version": "0.13.0",
              "runtime_identifier": "win-x64",
              "msi_url": "https://example.test/downloads/KoeNote-v0.13.0-win-x64.msi",
              "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            }
            """);

        var result = await service.CheckAsync();

        Assert.True(result.IsConfigured);
        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.LatestRelease);
    }

    [Fact]
    public async Task CheckAsync_IgnoresIncompatibleRuntime()
    {
        var service = CreateService("""
            {
              "schema_version": 1,
              "version": "0.14.0",
              "runtime_identifier": "win-arm64",
              "msi_url": "https://example.test/downloads/KoeNote-v0.14.0-win-arm64.msi",
              "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
            }
            """);

        var result = await service.CheckAsync();

        Assert.True(result.IsConfigured);
        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("win-arm64", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_RejectsInvalidSha256()
    {
        var service = CreateService("""
            {
              "schema_version": 1,
              "version": "0.14.0",
              "runtime_identifier": "win-x64",
              "msi_url": "https://example.test/downloads/KoeNote-v0.14.0-win-x64.msi",
              "sha256": "not-a-hash"
            }
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task CheckAsync_SkipsPrereleaseWhenPrereleaseUpdatesAreDisabled()
    {
        var service = CreateService("""
            {
              "schema_version": 1,
              "version": "0.14.0-beta.1",
              "runtime_identifier": "win-x64",
              "msi_url": "https://example.test/downloads/KoeNote-v0.14.0-beta.1-win-x64.msi",
              "sha256": "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd"
            }
            """);

        var result = await service.CheckAsync();

        Assert.True(result.IsConfigured);
        Assert.False(result.IsUpdateAvailable);
        Assert.Contains("prerelease", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckAsync_AllowsPrereleaseWhenEnabled()
    {
        var service = new UpdateCheckService(
            new HttpClient(new StaticJsonHandler("""
                {
                  "schema_version": 1,
                  "version": "0.14.0-beta.1",
                  "runtime_identifier": "win-x64",
                  "msi_url": "https://example.test/downloads/KoeNote-v0.14.0-beta.1-win-x64.msi",
                  "sha256": "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
                }
                """)),
            new UpdateCheckOptions(new Uri("https://example.test/latest.json"), "win-x64", IncludePrerelease: true),
            "0.13.0");

        var result = await service.CheckAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("0.14.0-beta.1", result.LatestRelease?.Version);
    }

    [Fact]
    public async Task CheckAsync_RejectsNonHttpsUrls()
    {
        var service = new UpdateCheckService(
            new HttpClient(new StaticJsonHandler("""
                {
                  "schema_version": 1,
                  "version": "0.14.0",
                  "runtime_identifier": "win-x64",
                  "msi_url": "https://example.test/downloads/KoeNote-v0.14.0-win-x64.msi",
                  "sha256": "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff"
                }
                """)),
            new UpdateCheckOptions(new Uri("http://example.test/latest.json"), "win-x64"),
            "0.13.0");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CheckAsync());
    }

    [Fact]
    public async Task CheckAsync_ReturnsNotConfiguredWhenLatestJsonUrlIsMissing()
    {
        var service = new UpdateCheckService(
            new HttpClient(new StaticJsonHandler("{}")),
            new UpdateCheckOptions(null, "win-x64"),
            "0.13.0");

        var result = await service.CheckAsync();

        Assert.False(result.IsConfigured);
        Assert.False(result.IsUpdateAvailable);
    }

    private static UpdateCheckService CreateService(string json)
    {
        return new UpdateCheckService(
            new HttpClient(new StaticJsonHandler(json)),
            new UpdateCheckOptions(new Uri("https://example.test/latest.json"), "win-x64"),
            "0.13.0");
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            };

            return Task.FromResult(response);
        }
    }
}
