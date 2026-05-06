using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateCheckOptions(
    Uri? LatestJsonUri,
    string RuntimeIdentifier,
    bool IncludePrerelease = false)
{
    public static UpdateCheckOptions FromEnvironment()
    {
        var latestJsonUrl = Environment.GetEnvironmentVariable("KOENOTE_UPDATE_LATEST_URL");
        return new UpdateCheckOptions(
            Uri.TryCreate(latestJsonUrl, UriKind.Absolute, out var uri) ? uri : null,
            RuntimeInformation.RuntimeIdentifier);
    }
}

public sealed record LatestReleaseInfo(
    string Version,
    Uri MsiUrl,
    string Sha256,
    Uri? Sha256Url,
    Uri? ReleaseNotesUrl,
    bool Mandatory,
    string RuntimeIdentifier,
    DateTimeOffset? PublishedAt);

public sealed record UpdateCheckResult(
    bool IsConfigured,
    bool IsUpdateAvailable,
    bool IsMandatory,
    string CurrentVersion,
    LatestReleaseInfo? LatestRelease,
    string Message)
{
    public static UpdateCheckResult NotConfigured(string currentVersion)
    {
        return new UpdateCheckResult(false, false, false, currentVersion, null, "Update check is not configured.");
    }
}

public interface IUpdateCheckService
{
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed class UpdateCheckService(
    HttpClient httpClient,
    UpdateCheckOptions options,
    string? currentVersion = null) : IUpdateCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var effectiveCurrentVersion = NormalizeVersion(currentVersion ?? GetCurrentVersion());
        if (options.LatestJsonUri is null)
        {
            return UpdateCheckResult.NotConfigured(effectiveCurrentVersion);
        }

        EnsureHttpsUri(options.LatestJsonUri, "latest.json URL");
        using var response = await httpClient.GetAsync(options.LatestJsonUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var manifest = await JsonSerializer.DeserializeAsync<LatestReleaseManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("latest.json is empty.");

        var latest = ValidateManifest(manifest, options.LatestJsonUri);
        if (!IsRuntimeCompatible(latest.RuntimeIdentifier, options.RuntimeIdentifier))
        {
            return new UpdateCheckResult(
                true,
                false,
                false,
                effectiveCurrentVersion,
                null,
                $"Latest release is for {latest.RuntimeIdentifier}, current runtime is {options.RuntimeIdentifier}.");
        }

        if (IsPrerelease(latest.Version) && !options.IncludePrerelease)
        {
            return new UpdateCheckResult(
                true,
                false,
                false,
                effectiveCurrentVersion,
                null,
                $"Latest release {latest.Version} is a prerelease and prerelease updates are disabled.");
        }

        var updateAvailable = IsNewerVersion(latest.Version, effectiveCurrentVersion);
        return new UpdateCheckResult(
            true,
            updateAvailable,
            updateAvailable && latest.Mandatory,
            effectiveCurrentVersion,
            updateAvailable ? latest : null,
            updateAvailable
                ? $"KoeNote {latest.Version} is available."
                : $"KoeNote is up to date ({effectiveCurrentVersion}).");
    }

    private static LatestReleaseInfo ValidateManifest(LatestReleaseManifest manifest, Uri manifestUri)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported latest.json schema_version: {manifest.SchemaVersion}.");
        }

        var version = (manifest.Version ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("latest.json version is missing.");
        }

        if (!Uri.TryCreate(manifest.MsiUrl, UriKind.Absolute, out var msiUrl))
        {
            throw new InvalidOperationException("latest.json msi_url must be an absolute URL.");
        }
        EnsureHttpsUri(msiUrl, "latest.json msi_url");

        if (string.IsNullOrWhiteSpace(manifest.Sha256) ||
            !System.Text.RegularExpressions.Regex.IsMatch(manifest.Sha256, "^[0-9a-fA-F]{64}$"))
        {
            throw new InvalidOperationException("latest.json sha256 must be a 64-character hex string.");
        }

        Uri? sha256Url = null;
        if (!string.IsNullOrWhiteSpace(manifest.Sha256Url) &&
            !Uri.TryCreate(manifest.Sha256Url, UriKind.Absolute, out sha256Url))
        {
            throw new InvalidOperationException("latest.json sha256_url must be an absolute URL when provided.");
        }
        if (sha256Url is not null)
        {
            EnsureHttpsUri(sha256Url, "latest.json sha256_url");
        }

        Uri? releaseNotesUrl = null;
        if (!string.IsNullOrWhiteSpace(manifest.ReleaseNotesUrl) &&
            !Uri.TryCreate(manifest.ReleaseNotesUrl, UriKind.Absolute, out releaseNotesUrl))
        {
            throw new InvalidOperationException("latest.json release_notes_url must be an absolute URL when provided.");
        }
        if (releaseNotesUrl is not null)
        {
            EnsureHttpsUri(releaseNotesUrl, "latest.json release_notes_url");
        }

        return new LatestReleaseInfo(
            version,
            msiUrl,
            manifest.Sha256.ToLowerInvariant(),
            sha256Url,
            releaseNotesUrl ?? manifestUri,
            manifest.Mandatory,
            string.IsNullOrWhiteSpace(manifest.RuntimeIdentifier) ? "win-x64" : manifest.RuntimeIdentifier,
            manifest.PublishedAt);
    }

    private static bool IsRuntimeCompatible(string releaseRuntime, string currentRuntime)
    {
        return string.Equals(releaseRuntime, currentRuntime, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        return Version.TryParse(NormalizeVersion(latestVersion), out var latest) &&
            Version.TryParse(NormalizeVersion(currentVersion), out var current) &&
            latest > current;
    }

    private static bool IsPrerelease(string version)
    {
        return version.Contains('-', StringComparison.Ordinal);
    }

    private static void EnsureHttpsUri(Uri uri, string fieldName)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"{fieldName} must use HTTPS.");
        }
    }

    private static string GetCurrentVersion()
    {
        var assembly = typeof(UpdateCheckService).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return NormalizeVersion(informationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0");
    }

    private static string NormalizeVersion(string? version)
    {
        var value = (version ?? string.Empty).Trim();
        var metadataIndex = value.IndexOfAny(['+', '-']);
        if (metadataIndex >= 0)
        {
            value = value[..metadataIndex];
        }

        return value;
    }

    private sealed class LatestReleaseManifest
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("runtime_identifier")]
        public string RuntimeIdentifier { get; init; } = string.Empty;

        [JsonPropertyName("msi_url")]
        public string MsiUrl { get; init; } = string.Empty;

        [JsonPropertyName("sha256")]
        public string Sha256 { get; init; } = string.Empty;

        [JsonPropertyName("sha256_url")]
        public string? Sha256Url { get; init; }

        [JsonPropertyName("release_notes_url")]
        public string? ReleaseNotesUrl { get; init; }

        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; init; }

        [JsonPropertyName("published_at")]
        public DateTimeOffset? PublishedAt { get; init; }
    }
}
