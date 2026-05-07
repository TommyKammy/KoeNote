using System.IO;
using System.Text.Json.Serialization;

namespace KoeNote.App.Services.Models;

public sealed record ModelCatalog(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("catalog_version")] string CatalogVersion,
    [property: JsonPropertyName("models")] IReadOnlyList<ModelCatalogItem> Models,
    [property: JsonPropertyName("presets")] IReadOnlyList<ModelQualityPreset>? Presets = null);

public sealed record ModelQualityPreset(
    [property: JsonPropertyName("preset_id")] string PresetId,
    [property: JsonPropertyName("quality_label")] string QualityLabel,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("asr_model_id")] string AsrModelId,
    [property: JsonPropertyName("review_model_id")] string ReviewModelId,
    [property: JsonPropertyName("badges")] IReadOnlyList<string> Badges,
    [property: JsonPropertyName("recommended_for")] IReadOnlyList<string> RecommendedFor)
{
    public override string ToString()
    {
        return DisplayName;
    }
}

public sealed record ModelCatalogItem(
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("family")] string Family,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("engine_id")] string EngineId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("language")] IReadOnlyList<string> Language,
    [property: JsonPropertyName("recommended_for")] IReadOnlyList<string> RecommendedFor,
    [property: JsonPropertyName("runtime")] ModelRuntimeSpec Runtime,
    [property: JsonPropertyName("download")] ModelDownloadSpec Download,
    [property: JsonPropertyName("license")] ModelLicenseSpec License,
    [property: JsonPropertyName("requirements")] ModelRequirements Requirements,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("size_bytes")] long? SizeBytes = null,
    [property: JsonPropertyName("quality_labels")] IReadOnlyList<string>? QualityLabels = null);

public sealed record ModelRuntimeSpec(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("package_id")] string PackageId);

public sealed record ModelDownloadSpec(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("sha256")] string? Sha256);

public sealed record ModelLicenseSpec(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("url")] string? Url);

public sealed record ModelRequirements(
    [property: JsonPropertyName("gpu_required")] bool GpuRequired,
    [property: JsonPropertyName("recommended_vram_gb")] int RecommendedVramGb,
    [property: JsonPropertyName("python_required")] bool PythonRequired);

public sealed record InstalledModel(
    string ModelId,
    string Role,
    string EngineId,
    string DisplayName,
    string? Family,
    string? Version,
    string FilePath,
    string? ManifestPath,
    long? SizeBytes,
    string? Sha256,
    bool Verified,
    string? LicenseName,
    string SourceType,
    DateTimeOffset InstalledAt,
    DateTimeOffset? LastVerifiedAt,
    string Status);

public sealed record InstalledRuntime(
    string RuntimeId,
    string RuntimeType,
    string DisplayName,
    string? Version,
    string InstallPath,
    bool Verified,
    string SourceType,
    DateTimeOffset InstalledAt,
    string Status);

public sealed record ModelDownloadJob(
    string DownloadId,
    string ModelId,
    string Url,
    string TargetPath,
    string TempPath,
    string Status,
    long? BytesTotal,
    long BytesDownloaded,
    string? Sha256Expected,
    string? Sha256Actual,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ModelCatalogEntry(
    ModelCatalogItem CatalogItem,
    InstalledModel? InstalledModel,
    ModelDownloadJob? LatestDownloadJob = null)
{
    public string ModelId => CatalogItem.ModelId;

    public string DisplayName => CatalogItem.DisplayName;

    public string SetupDisplayName => IsInstalled
        ? $"{DisplayName} (導入済み)"
        : DisplayName;

    public string Role => CatalogItem.Role;

    public string EngineId => CatalogItem.EngineId;

    public string InstallState => InstalledModel is null
        ? "Available"
        : IsInstalled
            ? InstalledModel.Status
            : "missing";

    public bool IsInstalled => InstalledModel is not null && InstalledModelPathExists;

    public bool IsVerified => InstalledModel?.Verified == true && InstalledModelPathExists;

    public string LicenseName => CatalogItem.License.Name;

    public string SizeSummary => (InstalledModel?.SizeBytes ?? CatalogItem.SizeBytes) is { } sizeBytes
        ? FormatSize(sizeBytes)
        : "Size TBD";

    public string RuntimeRequirement => $"{CatalogItem.Runtime.Type}, VRAM {CatalogItem.Requirements.RecommendedVramGb}GB, Python {(CatalogItem.Requirements.PythonRequired ? "required" : "not required")}";

    public string QualityLabelSummary => CatalogItem.QualityLabels is { Count: > 0 } labels
        ? string.Join(", ", labels)
        : string.Empty;

    public string DownloadState => LatestDownloadJob is null
        ? CatalogItem.Download.Type
        : LatestDownloadJob.Status;

    public bool IsDirectDownloadSupported
    {
        get
        {
            var type = CatalogItem.Download.Type;
            var url = CatalogItem.Download.Url;
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            if (type.Equals("huggingface", StringComparison.OrdinalIgnoreCase))
            {
                return url.Contains("/resolve/", StringComparison.OrdinalIgnoreCase) ||
                    IsHuggingFaceRepositoryUrl(url);
            }

            return type.Equals("https", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("direct", StringComparison.OrdinalIgnoreCase) ||
                type.Equals("direct_file", StringComparison.OrdinalIgnoreCase);
        }
    }

    public string DownloadProgressSummary
    {
        get
        {
            if (LatestDownloadJob is null)
            {
                return string.IsNullOrWhiteSpace(CatalogItem.Download.Url) ? "No URL" : "Ready";
            }

            if (LatestDownloadJob is { } job && UsableDownloadTotal is { } totalBytes)
            {
                var percent = job.BytesDownloaded * 100d / totalBytes;
                return $"{job.Status} {percent:0}%";
            }

            return LatestDownloadJob.BytesDownloaded > 0
                ? $"{LatestDownloadJob.Status} {FormatSize(LatestDownloadJob.BytesDownloaded)}"
                : LatestDownloadJob.Status;
        }
    }

    public double DownloadProgressPercent => LatestDownloadJob is { } job && UsableDownloadTotal is { } totalBytes
        ? Math.Clamp(job.BytesDownloaded * 100d / totalBytes, 0, 100)
        : 0;

    public bool HasKnownDownloadProgress => UsableDownloadTotal is not null;

    public bool IsDownloadProgressIndeterminate => IsDownloadActive && !HasKnownDownloadProgress;

    public bool IsDownloadActive => LatestDownloadJob is { Status: "running" or "paused" };

    public string DownloadBytesSummary => LatestDownloadJob is null
        ? string.Empty
        : UsableDownloadTotal is { } totalBytes
            ? $"{FormatSize(LatestDownloadJob.BytesDownloaded)} / {FormatSize(totalBytes)}"
            : FormatSize(LatestDownloadJob.BytesDownloaded);

    public string? DownloadError => LatestDownloadJob?.ErrorMessage;

    public override string ToString()
    {
        return SetupDisplayName;
    }

    private long? UsableDownloadTotal => LatestDownloadJob is { BytesTotal: > 0 } job &&
        job.BytesDownloaded <= job.BytesTotal.Value
            ? job.BytesTotal
            : null;

    private bool InstalledModelPathExists => InstalledModel is not null &&
        (File.Exists(InstalledModel.FilePath) || Directory.Exists(InstalledModel.FilePath));

    private static bool IsHuggingFaceRepositoryUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length >= 2 &&
            !segments[0].Equals("models", StringComparison.OrdinalIgnoreCase) &&
            !segments.Contains("resolve", StringComparer.OrdinalIgnoreCase);
    }

    private static string FormatSize(long sizeBytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)sizeBytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }

        return $"{value:0.#} {units[index]}";
    }
}
