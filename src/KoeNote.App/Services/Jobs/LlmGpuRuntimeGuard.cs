using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Services.Jobs;

internal static class LlmGpuRuntimeGuard
{
    public static void ThrowIfRequiredRuntimeMissing(
        AppPaths paths,
        ISetupHostResourceProbe? hostResourceProbe,
        LlmRuntimeProfile profile)
    {
        if (hostResourceProbe?.GetResources().NvidiaGpuDetected != true ||
            profile.GpuLayers <= 0 ||
            CudaReviewRuntimeLayout.HasPackage(paths))
        {
            return;
        }

        throw new ReviewWorkerException(
            ReviewFailureCategory.MissingRuntime,
            $"NVIDIA GPU was detected, but Review GPU runtime is not ready. Open Setup Wizard and reinstall Review GPU runtime. Missing: {string.Join("; ", CudaReviewRuntimeLayout.GetMissingPackageItems(paths))}");
    }
}
