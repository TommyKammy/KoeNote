using System.Text.Json.Serialization;

namespace KoeNote.App.Services.Models;

public sealed record ModelCatalog(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("catalog_version")] string CatalogVersion,
    [property: JsonPropertyName("models")] IReadOnlyList<ModelCatalogItem> Models);

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
    [property: JsonPropertyName("size_bytes")] long? SizeBytes = null);

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
    InstalledModel? InstalledModel)
{
    public string ModelId => CatalogItem.ModelId;

    public string DisplayName => CatalogItem.DisplayName;

    public string Role => CatalogItem.Role;

    public string EngineId => CatalogItem.EngineId;

    public string InstallState => InstalledModel is null ? "Available" : InstalledModel.Status;

    public bool IsInstalled => InstalledModel is not null;

    public bool IsVerified => InstalledModel?.Verified == true;

    public string LicenseName => CatalogItem.License.Name;

    public string SizeSummary => (InstalledModel?.SizeBytes ?? CatalogItem.SizeBytes) is { } sizeBytes
        ? FormatSize(sizeBytes)
        : "Size TBD";

    public string RuntimeRequirement => $"{CatalogItem.Runtime.Type}, VRAM {CatalogItem.Requirements.RecommendedVramGb}GB, Python {(CatalogItem.Requirements.PythonRequired ? "required" : "not required")}";

    public string DownloadState => CatalogItem.Download.Type;

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
