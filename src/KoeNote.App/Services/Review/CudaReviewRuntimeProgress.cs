namespace KoeNote.App.Services.Review;

internal static class CudaReviewRuntimeProgress
{
    public static void Report(IProgress<RuntimeInstallProgress>? progress, string stageText, string message)
    {
        progress?.Report(new RuntimeInstallProgress(stageText, message));
    }
}
