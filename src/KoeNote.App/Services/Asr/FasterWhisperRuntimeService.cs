using System.IO;
using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Services.Asr;

public sealed class FasterWhisperRuntimeService(
    AppPaths paths,
    ExternalProcessRunner processRunner,
    PythonRuntimeResolver? pythonRuntimeResolver = null)
{
    public const string PackageName = "faster-whisper";
    public const string PackageImportName = "faster_whisper";
    public const string RequiredPackageVersion = "1.2.1";
    public const string PackageSpec = PackageName + "==" + RequiredPackageVersion;
    public const string FailureCategoryPythonSourceUnavailable = "python-source-unavailable";
    public const string FailureCategoryVenvCreationFailed = "venv-creation-failed";
    public const string FailureCategoryPipInstallFailed = "pip-install-failed";
    public const string FailureCategoryNetworkUnavailable = "network-unavailable";
    public const string FailureCategoryPackageCheckFailed = "package-check-failed";
    private readonly PythonRuntimeResolver _pythonRuntimeResolver = pythonRuntimeResolver ?? new PythonRuntimeResolver(paths, processRunner);

    public async Task<FasterWhisperRuntimeStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(paths.AsrPythonPath))
            {
                return new FasterWhisperRuntimeStatus(false, "faster-whisper runtime is not installed.", paths.AsrPythonEnvironment);
            }

            var result = await processRunner.RunAsync(
                paths.AsrPythonPath,
                ["-c", "import importlib.metadata as md; import faster_whisper; print(md.version('faster-whisper'))"],
                TimeSpan.FromMinutes(2),
                cancellationToken);
            var detail = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError.Trim()
                : result.StandardOutput.Trim();

            if (result.ExitCode != 0)
            {
                return new FasterWhisperRuntimeStatus(false, detail, paths.AsrPythonEnvironment);
            }

            return string.Equals(detail, RequiredPackageVersion, StringComparison.OrdinalIgnoreCase)
                ? new FasterWhisperRuntimeStatus(true, detail, paths.AsrPythonEnvironment)
                : new FasterWhisperRuntimeStatus(
                    false,
                    $"faster-whisper {detail} is installed, but {RequiredPackageVersion} is required.",
                    paths.AsrPythonEnvironment);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new FasterWhisperRuntimeStatus(false, exception.Message, paths.AsrPythonEnvironment);
        }
    }

    public async Task<FasterWhisperRuntimePreflightStatus> CheckInstallPreflightAsync(CancellationToken cancellationToken = default)
    {
        var source = await _pythonRuntimeResolver.ResolveInstallSourceAsync(cancellationToken);
        if (!source.IsFound || source.Command is null)
        {
            return new FasterWhisperRuntimePreflightStatus(
                false,
                source.Message,
                paths.AsrPythonEnvironment,
                FailureCategoryPythonSourceUnavailable);
        }

        var pipResult = await processRunner.RunAsync(
            source.Command.FileName,
            source.Command.BuildArguments("-m", "pip", "--version"),
            TimeSpan.FromMinutes(1),
            cancellationToken);
        if (pipResult.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(pipResult.StandardError)
                ? pipResult.StandardOutput.Trim()
                : pipResult.StandardError.Trim();
            return new FasterWhisperRuntimePreflightStatus(
                false,
                $"Bundled Python was found, but pip is unavailable: {detail}",
                paths.AsrPythonEnvironment,
                FailureCategoryPipInstallFailed);
        }

        return new FasterWhisperRuntimePreflightStatus(
            true,
            $"Ready to install {PackageSpec} using {source.Command.DisplayName} {source.Command.Version}.",
            paths.AsrPythonEnvironment,
            string.Empty);
    }

    public async Task<FasterWhisperRuntimeInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(paths.PythonEnvironments);
            var runtime = await EnsureManagedPythonRuntimeAsync(cancellationToken);
            if (!runtime.IsSucceeded)
            {
                return FasterWhisperRuntimeInstallResult.Failed(
                    runtime.Message,
                    paths.AsrPythonEnvironment,
                    runtime.FailureCategory);
            }

            var result = await processRunner.RunAsync(
                paths.AsrPythonPath,
                ["-m", "pip", "install", "--upgrade", PackageSpec],
                TimeSpan.FromHours(1),
                cancellationToken);
            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim();
                return FasterWhisperRuntimeInstallResult.Failed(
                    BuildPipInstallFailureMessage(error),
                    paths.AsrPythonEnvironment,
                    ClassifyPipInstallFailure(error));
            }

            var status = await CheckAsync(cancellationToken);
            return status.IsAvailable
                ? FasterWhisperRuntimeInstallResult.Succeeded($"faster-whisper runtime installed: {status.Detail}", paths.AsrPythonEnvironment)
                : FasterWhisperRuntimeInstallResult.Failed(
                    $"faster-whisper installed but package check failed: {status.Detail}",
                    paths.AsrPythonEnvironment,
                    FailureCategoryPackageCheckFailed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return FasterWhisperRuntimeInstallResult.Failed(
                $"faster-whisper install failed: {exception.Message}",
                paths.AsrPythonEnvironment,
                FailureCategoryPipInstallFailed);
        }
    }

    private async Task<FasterWhisperManagedRuntimeResult> EnsureManagedPythonRuntimeAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(paths.AsrPythonPath))
        {
            var probe = await processRunner.RunAsync(
                paths.AsrPythonPath,
                ["-c", "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')"],
                TimeSpan.FromMinutes(1),
                cancellationToken);
            var versionText = string.IsNullOrWhiteSpace(probe.StandardOutput)
                ? probe.StandardError.Trim()
                : probe.StandardOutput.Trim();
            if (probe.ExitCode == 0 &&
                Version.TryParse(versionText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(), out var version) &&
                PythonRuntimeResolver.IsSupportedVersion(version))
            {
                return FasterWhisperManagedRuntimeResult.Succeeded();
            }

            Directory.Delete(paths.AsrPythonEnvironment, recursive: true);
        }

        var source = await _pythonRuntimeResolver.ResolveInstallSourceAsync(cancellationToken);
        if (!source.IsFound || source.Command is null)
        {
            return FasterWhisperManagedRuntimeResult.Failed(source.Message, FailureCategoryPythonSourceUnavailable);
        }

        var createResult = await processRunner.RunAsync(
            source.Command.FileName,
            source.Command.BuildArguments("-m", "venv", paths.AsrPythonEnvironment),
            TimeSpan.FromMinutes(10),
            cancellationToken);
        if (createResult.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(createResult.StandardError)
                ? createResult.StandardOutput.Trim()
                : createResult.StandardError.Trim();
            return FasterWhisperManagedRuntimeResult.Failed(
                $"ASR Python venv creation failed: {error}",
                FailureCategoryVenvCreationFailed);
        }

        return File.Exists(paths.AsrPythonPath)
            ? FasterWhisperManagedRuntimeResult.Succeeded()
            : FasterWhisperManagedRuntimeResult.Failed("ASR Python venv was created but python.exe was not found.", FailureCategoryVenvCreationFailed);
    }

    private static string BuildPipInstallFailureMessage(string error)
    {
        return ClassifyPipInstallFailure(error) switch
        {
            FailureCategoryNetworkUnavailable =>
                $"faster-whisper could not be downloaded. Check the network connection or proxy settings, then retry. Details: {error}",
            _ => $"faster-whisper install failed: {error}"
        };
    }

    private static string ClassifyPipInstallFailure(string error)
    {
        if (error.Contains("NameResolution", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Temporary failure", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("Failed to establish a new connection", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("No route to host", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("proxy", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("SSL", StringComparison.OrdinalIgnoreCase))
        {
            return FailureCategoryNetworkUnavailable;
        }

        return FailureCategoryPipInstallFailed;
    }
}

public sealed record FasterWhisperRuntimeStatus(bool IsAvailable, string Detail, string InstallPath);

public sealed record FasterWhisperRuntimePreflightStatus(
    bool IsReady,
    string Message,
    string InstallPath,
    string FailureCategory);

public sealed record FasterWhisperRuntimeInstallResult(
    bool IsSucceeded,
    string Message,
    string InstallPath,
    string FailureCategory)
{
    public static FasterWhisperRuntimeInstallResult Succeeded(string message, string installPath)
    {
        return new FasterWhisperRuntimeInstallResult(true, message, installPath, string.Empty);
    }

    public static FasterWhisperRuntimeInstallResult Failed(string message, string installPath, string failureCategory)
    {
        return new FasterWhisperRuntimeInstallResult(false, message, installPath, failureCategory);
    }
}

internal sealed record FasterWhisperManagedRuntimeResult(
    bool IsSucceeded,
    string Message,
    string FailureCategory)
{
    public static FasterWhisperManagedRuntimeResult Succeeded()
    {
        return new FasterWhisperManagedRuntimeResult(true, string.Empty, string.Empty);
    }

    public static FasterWhisperManagedRuntimeResult Failed(string message, string failureCategory)
    {
        return new FasterWhisperManagedRuntimeResult(false, message, failureCategory);
    }
}
