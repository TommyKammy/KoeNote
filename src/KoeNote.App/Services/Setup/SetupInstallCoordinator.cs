namespace KoeNote.App.Services.Setup;

internal sealed class SetupInstallCoordinator
{
    public async Task<bool> RunPresetInstallAsync(
        SetupInstallSequence sequence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await sequence.InstallSelectedModelsAsync(cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await sequence.InstallFasterWhisperRuntimeAsync(cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!sequence.ValidateReviewRuntime())
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await sequence.InstallAsrCudaRuntimeAsync(cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await sequence.InstallDiarizationRuntimeAsync(cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!await sequence.InstallCudaReviewRuntimeAsync(cancellationToken))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await sequence.InstallTernaryReviewRuntimeAsync(cancellationToken);
    }
}

internal sealed record SetupInstallSequence(
    Func<CancellationToken, Task<bool>> InstallSelectedModelsAsync,
    Func<CancellationToken, Task<bool>> InstallFasterWhisperRuntimeAsync,
    Func<bool> ValidateReviewRuntime,
    Func<CancellationToken, Task<bool>> InstallAsrCudaRuntimeAsync,
    Func<CancellationToken, Task<bool>> InstallDiarizationRuntimeAsync,
    Func<CancellationToken, Task<bool>> InstallCudaReviewRuntimeAsync,
    Func<CancellationToken, Task<bool>> InstallTernaryReviewRuntimeAsync);
