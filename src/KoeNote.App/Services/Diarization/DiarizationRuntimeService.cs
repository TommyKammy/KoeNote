using System.IO;

namespace KoeNote.App.Services.Diarization;

public sealed class DiarizationRuntimeService(
    AppPaths paths,
    ExternalProcessRunner processRunner)
{
    public const string PackageSpec = "diarize==0.1.1";

    public async Task<DiarizationRuntimeStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await processRunner.RunAsync(
                "python",
                ["-c", "import diarize; print(getattr(diarize, '__version__', 'installed'))"],
                TimeSpan.FromMinutes(2),
                cancellationToken,
                PythonRuntimeEnvironment.Build(paths));
            var detail = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError.Trim()
                : result.StandardOutput.Trim();
            return new DiarizationRuntimeStatus(result.ExitCode == 0, detail, paths.PythonPackages);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DiarizationRuntimeStatus(false, exception.Message, paths.PythonPackages);
        }
    }

    public async Task<DiarizationRuntimeInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(paths.PythonPackages);
            var result = await processRunner.RunAsync(
                "python",
                [
                    "-m",
                    "pip",
                    "install",
                    "--upgrade",
                    "--target",
                    paths.PythonPackages,
                    PackageSpec
                ],
                TimeSpan.FromHours(1),
                cancellationToken);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim();
                return new DiarizationRuntimeInstallResult(false, $"diarize install failed: {error}", paths.PythonPackages);
            }

            var status = await CheckAsync(cancellationToken);
            return status.IsAvailable
                ? new DiarizationRuntimeInstallResult(true, $"diarize runtime installed: {status.Detail}", paths.PythonPackages)
                : new DiarizationRuntimeInstallResult(false, $"diarize installed but import check failed: {status.Detail}", paths.PythonPackages);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DiarizationRuntimeInstallResult(false, $"diarize install failed: {exception.Message}", paths.PythonPackages);
        }
    }
}

public sealed record DiarizationRuntimeStatus(bool IsAvailable, string Detail, string InstallPath);

public sealed record DiarizationRuntimeInstallResult(bool IsSucceeded, string Message, string InstallPath);
