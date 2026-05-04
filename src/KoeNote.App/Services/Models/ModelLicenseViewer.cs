namespace KoeNote.App.Services.Models;

public sealed class ModelLicenseViewer(ModelCatalogService catalogService)
{
    public string BuildLicenseSummary(string modelId)
    {
        var model = catalogService.LoadBuiltInCatalog()
            .Models
            .FirstOrDefault(item => string.Equals(item.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

        if (model is null)
        {
            return $"Model not found in catalog: {modelId}";
        }

        return $"""
            {model.DisplayName}
            License: {model.License.Name}
            URL: {model.License.Url ?? "not specified"}
            Runtime: {model.Runtime.Type} ({model.Runtime.PackageId})
            Download: {model.Download.Type} {model.Download.Url ?? ""}
            """;
    }
}
