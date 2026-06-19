using System.IO;

namespace KoeNote.App.Services.Review;

internal sealed class CudaReviewRuntimeFileMaintenance(AppPaths paths)
{
    public CudaReviewRuntimeInstallResult? TryRemovePersistedCudaBridge()
    {
        if (!CudaReviewRuntimeLayoutProbe.HasCudaBridge(paths.ReviewRuntimeDirectory))
        {
            return null;
        }

        try
        {
            if (!Directory.Exists(paths.CudaReviewRuntimeDirectory))
            {
                return null;
            }

            foreach (var bridgePath in Directory.EnumerateFiles(paths.CudaReviewRuntimeDirectory, "ggml-cuda*.dll", SearchOption.TopDirectoryOnly))
            {
                File.Delete(bridgePath);
            }

            return null;
        }
        catch (IOException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime bridge cleanup failed: {exception.Message}",
                paths.CudaReviewRuntimeDirectory,
                CudaReviewRuntimeService.FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime bridge cleanup failed: {exception.Message}",
                paths.CudaReviewRuntimeDirectory,
                CudaReviewRuntimeService.FailureCategoryInstallFailed);
        }
    }

    public CudaReviewRuntimeInstallResult? TryDeleteMatchingFiles(string sourceDirectory, IReadOnlyCollection<string> patterns)
    {
        try
        {
            RuntimeInstallFileOps.DeleteMatchingFiles(sourceDirectory, patterns);
            return null;
        }
        catch (IOException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime cleanup failed: {exception.Message}",
                paths.CudaReviewRuntimeDirectory,
                CudaReviewRuntimeService.FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime cleanup failed: {exception.Message}",
                paths.CudaReviewRuntimeDirectory,
                CudaReviewRuntimeService.FailureCategoryInstallFailed);
        }
    }

    public CudaReviewRuntimeInstallResult? TryMigrateMatchingFiles(
        string sourceDirectory,
        string destinationDirectory,
        IReadOnlyCollection<string> patterns)
    {
        try
        {
            RuntimeInstallFileOps.CopyMatchingFiles(sourceDirectory, destinationDirectory, patterns, deleteSourceFiles: true);
            return null;
        }
        catch (IOException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime migration failed: {exception.Message}",
                destinationDirectory,
                CudaReviewRuntimeService.FailureCategoryInstallFailed);
        }
        catch (UnauthorizedAccessException exception)
        {
            return CudaReviewRuntimeInstallResult.Failed(
                $"CUDA review runtime migration failed: {exception.Message}",
                destinationDirectory,
                CudaReviewRuntimeService.FailureCategoryInstallFailed);
        }
    }
}
