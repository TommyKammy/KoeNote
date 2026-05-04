using System.IO;

namespace KoeNote.App.Services.Models;

public sealed class ModelInstallService(
    AppPaths paths,
    InstalledModelRepository installedModelRepository,
    ModelVerificationService verificationService)
{
    public InstalledModel RegisterLocalModel(
        ModelCatalogItem catalogItem,
        string modelPath,
        string sourceType = "local_file",
        string? manifestPath = null)
    {
        var verification = verificationService.VerifyPath(modelPath, catalogItem.Download.Sha256);
        var now = DateTimeOffset.Now;
        var model = new InstalledModel(
            catalogItem.ModelId,
            catalogItem.Role,
            catalogItem.EngineId,
            catalogItem.DisplayName,
            catalogItem.Family,
            Version: null,
            modelPath,
            ResolveManifestPath(modelPath, manifestPath),
            CalculateSizeBytes(modelPath),
            verification.Sha256,
            verification.IsVerified,
            catalogItem.License.Name,
            sourceType,
            now,
            verification.IsVerified ? now : null,
            verification.IsVerified ? "installed" : "verification_failed");

        installedModelRepository.UpsertInstalledModel(model);
        return model;
    }

    public InstalledModel RegisterDownloadedModel(ModelCatalogItem catalogItem, string modelPath)
    {
        return RegisterLocalModel(catalogItem, modelPath, sourceType: "download");
    }

    public bool DeleteRegistration(string modelId)
    {
        if (installedModelRepository.FindInstalledModel(modelId) is null)
        {
            return false;
        }

        installedModelRepository.DeleteInstalledModel(modelId);
        return true;
    }

    public string GetDefaultInstallPath(ModelCatalogItem catalogItem)
    {
        if (catalogItem.Role.Equals("review", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                paths.DefaultModelStorageRoot,
                catalogItem.Role,
                catalogItem.ModelId,
                ResolveDownloadFileName(catalogItem));
        }

        return Path.Combine(paths.DefaultModelStorageRoot, catalogItem.Role, catalogItem.ModelId);
    }

    private static string ResolveDownloadFileName(ModelCatalogItem catalogItem)
    {
        if (catalogItem.Download.Url is { Length: > 0 } url &&
            Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var fileName = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return $"{catalogItem.ModelId}.gguf";
    }

    private static long? CalculateSizeBytes(string path)
    {
        if (File.Exists(path))
        {
            return new FileInfo(path).Length;
        }

        if (!Directory.Exists(path))
        {
            return null;
        }

        return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
            .Sum(static file => new FileInfo(file).Length);
    }

    private static string? ResolveManifestPath(string modelPath, string? explicitManifestPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitManifestPath) && File.Exists(explicitManifestPath))
        {
            return explicitManifestPath;
        }

        if (Directory.Exists(modelPath))
        {
            var modelManifest = Path.Combine(modelPath, "model.json");
            if (File.Exists(modelManifest))
            {
                return modelManifest;
            }

            var packManifest = Path.Combine(modelPath, "modelpack.json");
            return File.Exists(packManifest) ? packManifest : null;
        }

        var sidecar = $"{modelPath}.json";
        return File.Exists(sidecar) ? sidecar : null;
    }
}
