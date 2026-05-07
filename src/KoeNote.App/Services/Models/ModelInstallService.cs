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

    public ModelFileDeleteResult DeleteModelFiles(string modelId)
    {
        var installed = installedModelRepository.FindInstalledModel(modelId);
        if (installed is null)
        {
            return new ModelFileDeleteResult(modelId, string.Empty, 0, false);
        }

        var modelPath = Path.GetFullPath(installed.FilePath);
        var storageRoot = ResolveManagedStorageRoot(installed, modelPath);
        if (storageRoot is null)
        {
            throw new InvalidOperationException($"Model path is outside KoeNote model storage and was not deleted: {modelPath}");
        }

        var deletedBytes = CalculateSizeBytes(modelPath) ?? 0;
        if (File.Exists(modelPath))
        {
            File.Delete(modelPath);
            DeleteEmptyParentDirectories(Path.GetDirectoryName(modelPath), storageRoot);
        }
        else if (Directory.Exists(modelPath))
        {
            Directory.Delete(modelPath, recursive: true);
            DeleteEmptyParentDirectories(Path.GetDirectoryName(modelPath), storageRoot);
        }

        installedModelRepository.DeleteInstalledModel(modelId);
        return new ModelFileDeleteResult(modelId, modelPath, deletedBytes, true);
    }

    public string GetDefaultInstallPath(ModelCatalogItem catalogItem)
    {
        return GetDefaultInstallPath(catalogItem, paths.DefaultModelStorageRoot);
    }

    public string GetDefaultInstallPath(ModelCatalogItem catalogItem, string storageRoot)
    {
        if (catalogItem.Role.Equals("review", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(
                storageRoot,
                catalogItem.Role,
                catalogItem.ModelId,
                ResolveDownloadFileName(catalogItem));
        }

        return Path.Combine(storageRoot, catalogItem.Role, catalogItem.ModelId);
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

    private string? ResolveManagedStorageRoot(InstalledModel installed, string path)
    {
        var modelStorageRoot = Path.GetFullPath(paths.DefaultModelStorageRoot);
        var rootWithSeparator = modelStorageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        if (path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return modelStorageRoot;
        }

        if (!installed.SourceType.Equals("download", StringComparison.OrdinalIgnoreCase) &&
            !installed.SourceType.Equals("model_pack", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var modelDirectory = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        if (string.IsNullOrWhiteSpace(modelDirectory) ||
            !Path.GetFileName(modelDirectory).Equals(installed.ModelId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var roleDirectory = Path.GetDirectoryName(modelDirectory);
        if (string.IsNullOrWhiteSpace(roleDirectory) ||
            !Path.GetFileName(roleDirectory).Equals(installed.Role, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetDirectoryName(roleDirectory);
    }

    private static void DeleteEmptyParentDirectories(string? startDirectory, string storageRoot)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            return;
        }

        var modelStorageRoot = Path.GetFullPath(storageRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Path.GetFullPath(startDirectory);
        while (!current.Equals(modelStorageRoot, StringComparison.OrdinalIgnoreCase) &&
            IsUnderStorageRoot(current, modelStorageRoot) &&
            Directory.Exists(current) &&
            !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent))
            {
                break;
            }

            current = parent;
        }
    }

    private static bool IsUnderStorageRoot(string path, string storageRoot)
    {
        var rootWithSeparator = storageRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
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

public sealed record ModelFileDeleteResult(string ModelId, string FilePath, long DeletedBytes, bool DeletedRegistration);
