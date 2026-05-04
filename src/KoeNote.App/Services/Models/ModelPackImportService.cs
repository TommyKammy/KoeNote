using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KoeNote.App.Services.Models;

public sealed class ModelPackImportService(
    AppPaths paths,
    ModelCatalogService catalogService,
    ModelInstallService installService)
{
    public IReadOnlyList<InstalledModel> ImportModelPack(string modelPackPath)
    {
        if (!File.Exists(modelPackPath))
        {
            throw new FileNotFoundException("Model pack not found.", modelPackPath);
        }

        var packName = Path.GetFileNameWithoutExtension(modelPackPath);
        var finalRoot = Path.Combine(paths.UserModels, "model-packs", packName);
        var stagingRoot = Path.Combine(paths.UserModels, "model-packs", $".staging-{packName}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(stagingRoot);
            ZipFile.ExtractToDirectory(modelPackPath, stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "modelpack.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Model pack is missing modelpack.json.");
            }

            var manifest = JsonSerializer.Deserialize<ModelPackManifest>(File.ReadAllText(manifestPath))
                ?? throw new InvalidOperationException("Model pack manifest could not be read.");
            var catalogById = catalogService.LoadBuiltInCatalog()
                .Models
                .ToDictionary(model => model.ModelId, StringComparer.OrdinalIgnoreCase);
            var plannedModels = manifest.Models
                .Select(model => BuildPlan(stagingRoot, model, catalogById))
                .ToArray();

            var verificationService = new ModelVerificationService();
            foreach (var plan in plannedModels)
            {
                var verification = verificationService.VerifyPath(plan.StagingPath, plan.CatalogItem.Download.Sha256);
                if (!verification.IsVerified)
                {
                    throw new InvalidOperationException($"Model pack verification failed for {plan.CatalogItem.ModelId}: {verification.Message}");
                }
            }

            if (Directory.Exists(finalRoot))
            {
                Directory.Delete(finalRoot, recursive: true);
            }

            Directory.Move(stagingRoot, finalRoot);
            return plannedModels
                .Select(plan =>
                {
                    var finalPath = Path.Combine(finalRoot, plan.RelativePath);
                    return installService.RegisterLocalModel(
                        plan.CatalogItem,
                        finalPath,
                        "model_pack",
                        Path.Combine(finalRoot, "modelpack.json"));
                })
                .ToArray();
        }
        catch
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }

            throw;
        }
    }

    private static ModelPackInstallPlan BuildPlan(
        string stagingRoot,
        ModelPackModel model,
        IReadOnlyDictionary<string, ModelCatalogItem> catalogById)
    {
        var catalogItem = catalogById.TryGetValue(model.ModelId, out var builtInItem)
            ? builtInItem with
            {
                Download = builtInItem.Download with
                {
                    Sha256 = model.Sha256
                }
            }
            : new ModelCatalogItem(
                model.ModelId,
                Family: "model-pack",
                Role: "asr",
                model.EngineId,
                model.ModelId,
                ["ja"],
                [],
                new ModelRuntimeSpec("unknown", "unknown"),
                new ModelDownloadSpec("model_pack", null, model.Sha256),
                new ModelLicenseSpec("Model pack license", null),
                new ModelRequirements(false, 0, false),
                "available");

        return new ModelPackInstallPlan(catalogItem, model.RelativePath, Path.Combine(stagingRoot, model.RelativePath));
    }
}

internal sealed record ModelPackInstallPlan(
    ModelCatalogItem CatalogItem,
    string RelativePath,
    string StagingPath);

public sealed record ModelPackManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("pack_id")] string PackId,
    [property: JsonPropertyName("display_name")] string DisplayName,
    [property: JsonPropertyName("models")] IReadOnlyList<ModelPackModel> Models);

public sealed record ModelPackModel(
    [property: JsonPropertyName("model_id")] string ModelId,
    [property: JsonPropertyName("engine_id")] string EngineId,
    [property: JsonPropertyName("relative_path")] string RelativePath,
    [property: JsonPropertyName("sha256")] string? Sha256);
