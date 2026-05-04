using System.IO;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace KoeNote.App.Services.Models;

public sealed class ModelCatalogService(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public ModelCatalog LoadBuiltInCatalog()
    {
        if (!File.Exists(paths.ModelCatalogPath))
        {
            return new ModelCatalog(1, "fallback", []);
        }

        return LoadCatalogFile(paths.ModelCatalogPath);
    }

    public ModelCatalog LoadCatalogFile(string catalogPath)
    {
        return ParseCatalog(File.ReadAllText(catalogPath), catalogPath);
    }

    public async Task<ModelCatalog> LoadRemoteCatalogAsync(
        Uri catalogUri,
        HttpClient httpClient,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(catalogUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only HTTPS remote catalogs are allowed.");
        }

        var json = await httpClient.GetStringAsync(catalogUri, cancellationToken);
        return ParseCatalog(json, catalogUri.ToString());
    }

    public IReadOnlyList<ModelCatalogEntry> ListEntries()
    {
        var installed = new InstalledModelRepository(paths)
            .ListInstalledModels()
            .ToDictionary(model => model.ModelId, StringComparer.OrdinalIgnoreCase);

        return LoadBuiltInCatalog()
            .Models
            .Select(model => new ModelCatalogEntry(
                model,
                installed.TryGetValue(model.ModelId, out var installedModel) ? installedModel : null))
            .OrderBy(static entry => entry.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ModelCatalogEntry> ListEntries(ModelCatalog catalog)
    {
        var installed = new InstalledModelRepository(paths)
            .ListInstalledModels()
            .ToDictionary(model => model.ModelId, StringComparer.OrdinalIgnoreCase);

        return catalog.Models
            .Select(model => new ModelCatalogEntry(
                model,
                installed.TryGetValue(model.ModelId, out var installedModel) ? installedModel : null))
            .OrderBy(static entry => entry.Role, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string Serialize(ModelCatalog catalog) => JsonSerializer.Serialize(catalog, JsonOptions);

    private static ModelCatalog ParseCatalog(string json, string source)
    {
        var catalog = JsonSerializer.Deserialize<ModelCatalog>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Model catalog could not be read: {source}");
        if (catalog.SchemaVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported model catalog schema version: {catalog.SchemaVersion}");
        }

        return catalog;
    }
}
