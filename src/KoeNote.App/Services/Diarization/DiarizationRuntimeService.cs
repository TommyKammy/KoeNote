using System.IO;

namespace KoeNote.App.Services.Diarization;

public sealed class DiarizationRuntimeService(
    AppPaths paths,
    ExternalProcessRunner processRunner,
    PythonRuntimeResolver? pythonRuntimeResolver = null)
{
    public const string PackageName = "diarize";
    public const string RequiredPackageVersion = "0.1.2";
    public const string PackageSpec = PackageName + "==" + RequiredPackageVersion;
    public const string FailureCategoryPythonSourceUnavailable = "python-source-unavailable";
    public const string FailureCategoryVenvCreationFailed = "venv-creation-failed";
    public const string FailureCategoryPipInstallFailed = "pip-install-failed";
    public const string FailureCategoryNetworkUnavailable = "network-unavailable";
    public const string FailureCategoryTorchWheelUnavailable = "torch-wheel-unavailable";
    public const string FailureCategoryPackageCheckFailed = "package-check-failed";
    public const string FailureCategoryPackageDataMissing = "package-data-missing";
    private readonly PythonRuntimeResolver _pythonRuntimeResolver = pythonRuntimeResolver ?? new PythonRuntimeResolver(paths, processRunner);

    public async Task<DiarizationRuntimeStatus> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var runtime = await _pythonRuntimeResolver.ResolveInstalledRuntimeAsync(cancellationToken);
            if (!runtime.IsFound || runtime.Command is null)
            {
                return new DiarizationRuntimeStatus(false, runtime.Message, paths.DiarizationPythonEnvironment);
            }

            var result = await processRunner.RunAsync(
                runtime.Command.FileName,
                runtime.Command.BuildArguments("-c", "import importlib.metadata as md; print(md.version('diarize'))"),
                TimeSpan.FromMinutes(2),
                cancellationToken,
                runtime.Command.Environment);
            var detail = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError.Trim()
                : result.StandardOutput.Trim();
            if (result.ExitCode != 0)
            {
                return new DiarizationRuntimeStatus(false, detail, runtime.Command.InstallPath);
            }

            return string.Equals(detail, RequiredPackageVersion, StringComparison.OrdinalIgnoreCase)
                ? CheckRuntimeData(runtime.Command.InstallPath)
                : new DiarizationRuntimeStatus(
                    false,
                    $"diarize {detail} is installed, but {RequiredPackageVersion} is required.",
                    runtime.Command.InstallPath);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DiarizationRuntimeStatus(false, exception.Message, paths.DiarizationPythonEnvironment);
        }
    }

    public async Task<DiarizationRuntimeInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(paths.PythonEnvironments);
            var runtime = await EnsureManagedPythonRuntimeAsync(cancellationToken);
            if (!runtime.IsSucceeded || runtime.Command is null)
            {
                return DiarizationRuntimeInstallResult.Failed(
                    runtime.Message,
                    paths.DiarizationPythonEnvironment,
                    runtime.FailureCategory);
            }

            var result = await processRunner.RunAsync(
                runtime.Command.FileName,
                runtime.Command.BuildArguments(
                    "-m",
                    "pip",
                    "install",
                    "--upgrade",
                    PackageSpec),
                TimeSpan.FromHours(1),
                cancellationToken);

            if (result.ExitCode != 0)
            {
                var error = string.IsNullOrWhiteSpace(result.StandardError)
                    ? result.StandardOutput.Trim()
                    : result.StandardError.Trim();
                return DiarizationRuntimeInstallResult.Failed(
                    BuildPipInstallFailureMessage(error),
                    paths.DiarizationPythonEnvironment,
                    ClassifyPipInstallFailure(error));
            }

            var status = await CheckAsync(cancellationToken);
            return status.IsAvailable
                ? DiarizationRuntimeInstallResult.Succeeded($"diarize runtime installed: {status.Detail}", paths.DiarizationPythonEnvironment)
                : DiarizationRuntimeInstallResult.Failed(
                    $"diarize installed but package check failed: {status.Detail}",
                    paths.DiarizationPythonEnvironment,
                    string.IsNullOrWhiteSpace(status.FailureCategory)
                        ? FailureCategoryPackageCheckFailed
                        : status.FailureCategory);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return DiarizationRuntimeInstallResult.Failed(
                $"diarize install failed: {exception.Message}",
                paths.DiarizationPythonEnvironment,
                FailureCategoryPipInstallFailed);
        }
    }

    public async Task<DiarizationRuntimePreflightStatus> CheckInstallPreflightAsync(CancellationToken cancellationToken = default)
    {
        var source = await _pythonRuntimeResolver.ResolveInstallSourceAsync(cancellationToken);
        if (!source.IsFound || source.Command is null)
        {
            return new DiarizationRuntimePreflightStatus(
                false,
                source.Message,
                paths.DiarizationPythonEnvironment,
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
            return new DiarizationRuntimePreflightStatus(
                false,
                $"Bundled Python was found, but pip is unavailable: {detail}",
                paths.DiarizationPythonEnvironment,
                FailureCategoryPipInstallFailed);
        }

        return new DiarizationRuntimePreflightStatus(
            true,
            $"Ready to install {PackageSpec} using {source.Command.DisplayName} {source.Command.Version}.",
            paths.DiarizationPythonEnvironment,
            string.Empty);
    }

    private async Task<ManagedPythonRuntimeResult> EnsureManagedPythonRuntimeAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(paths.DiarizationPythonPath))
        {
            var runtime = await _pythonRuntimeResolver.ResolveInstalledRuntimeAsync(cancellationToken);
            if (runtime.IsFound && runtime.Command is not null)
            {
                return ManagedPythonRuntimeResult.Succeeded(runtime.Command);
            }

            Directory.Delete(paths.DiarizationPythonEnvironment, recursive: true);
        }

        var source = await _pythonRuntimeResolver.ResolveInstallSourceAsync(cancellationToken);
        if (!source.IsFound || source.Command is null)
        {
            return ManagedPythonRuntimeResult.Failed(source.Message, FailureCategoryPythonSourceUnavailable);
        }

        var createResult = await processRunner.RunAsync(
            source.Command.FileName,
            source.Command.BuildArguments("-m", "venv", paths.DiarizationPythonEnvironment),
            TimeSpan.FromMinutes(10),
            cancellationToken);
        if (createResult.ExitCode != 0)
        {
            var error = string.IsNullOrWhiteSpace(createResult.StandardError)
                ? createResult.StandardOutput.Trim()
                : createResult.StandardError.Trim();
            return ManagedPythonRuntimeResult.Failed(
                $"diarization Python venv creation failed: {error}",
                FailureCategoryVenvCreationFailed);
        }

        var managed = await _pythonRuntimeResolver.ResolveInstalledRuntimeAsync(cancellationToken);
        return managed.IsFound && managed.Command is not null
            ? ManagedPythonRuntimeResult.Succeeded(managed.Command)
            : ManagedPythonRuntimeResult.Failed(managed.Message, FailureCategoryVenvCreationFailed);
    }

    private static string BuildPipInstallFailureMessage(string error)
    {
        return ClassifyPipInstallFailure(error) switch
        {
            FailureCategoryTorchWheelUnavailable =>
                $"diarize install failed because compatible torch wheels were not available for this Python runtime. Use the bundled Python 3.12 runtime and retry. Details: {error}",
            FailureCategoryNetworkUnavailable =>
                $"diarize install failed because pip could not reach the package index. Check the network connection or proxy settings, then retry. Details: {error}",
            _ => $"diarize install failed: {error}"
        };
    }

    private static string ClassifyPipInstallFailure(string error)
    {
        if (error.Contains("torch", StringComparison.OrdinalIgnoreCase) &&
            (error.Contains("No matching distribution found", StringComparison.OrdinalIgnoreCase) ||
                error.Contains("Could not find a version that satisfies", StringComparison.OrdinalIgnoreCase)))
        {
            return FailureCategoryTorchWheelUnavailable;
        }

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

    private DiarizationRuntimeStatus CheckRuntimeData(string installPath)
    {
        var missing = string.Equals(installPath, paths.PythonPackages, StringComparison.OrdinalIgnoreCase)
            ? DiarizationRuntimeLayout.GetMissingLegacyRuntimeData(paths)
            : DiarizationRuntimeLayout.GetMissingManagedRuntimeData(paths);
        if (missing.Count == 0)
        {
            return new DiarizationRuntimeStatus(true, RequiredPackageVersion, installPath, string.Empty);
        }

        return new DiarizationRuntimeStatus(
            false,
            $"diarize {RequiredPackageVersion} is installed, but required runtime data is missing. Reinstall speaker diarization runtime. Missing: {string.Join("; ", missing)}",
            installPath,
            FailureCategoryPackageDataMissing);
    }
}

public sealed record DiarizationRuntimeStatus(
    bool IsAvailable,
    string Detail,
    string InstallPath,
    string FailureCategory = "");

public sealed record DiarizationRuntimePreflightStatus(
    bool IsReady,
    string Message,
    string InstallPath,
    string FailureCategory);

public sealed record DiarizationRuntimeInstallResult(
    bool IsSucceeded,
    string Message,
    string InstallPath,
    string FailureCategory)
{
    public static DiarizationRuntimeInstallResult Succeeded(string message, string installPath)
    {
        return new DiarizationRuntimeInstallResult(true, message, installPath, string.Empty);
    }

    public static DiarizationRuntimeInstallResult Failed(string message, string installPath, string failureCategory)
    {
        return new DiarizationRuntimeInstallResult(false, message, installPath, failureCategory);
    }
}

internal sealed record ManagedPythonRuntimeResult(
    bool IsSucceeded,
    PythonRuntimeCommand? Command,
    string Message,
    string FailureCategory)
{
    public static ManagedPythonRuntimeResult Succeeded(PythonRuntimeCommand command)
    {
        return new ManagedPythonRuntimeResult(true, command, string.Empty, string.Empty);
    }

    public static ManagedPythonRuntimeResult Failed(string message, string failureCategory)
    {
        return new ManagedPythonRuntimeResult(false, null, message, failureCategory);
    }
}
