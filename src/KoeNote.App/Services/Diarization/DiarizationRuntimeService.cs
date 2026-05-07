using System.IO;

namespace KoeNote.App.Services.Diarization;

public sealed class DiarizationRuntimeService(
    AppPaths paths,
    ExternalProcessRunner processRunner,
    PythonRuntimeResolver? pythonRuntimeResolver = null)
{
    public const string PackageSpec = "diarize==0.1.1";
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
                runtime.Command.BuildArguments("-c", "import diarize; print(getattr(diarize, '__version__', 'installed'))"),
                TimeSpan.FromMinutes(2),
                cancellationToken,
                runtime.Command.Environment);
            var detail = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError.Trim()
                : result.StandardOutput.Trim();
            return new DiarizationRuntimeStatus(result.ExitCode == 0, detail, runtime.Command.InstallPath);
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
                return new DiarizationRuntimeInstallResult(false, runtime.Message, paths.DiarizationPythonEnvironment);
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
                return new DiarizationRuntimeInstallResult(false, $"diarize install failed: {error}", paths.DiarizationPythonEnvironment);
            }

            var status = await CheckAsync(cancellationToken);
            return status.IsAvailable
                ? new DiarizationRuntimeInstallResult(true, $"diarize runtime installed: {status.Detail}", paths.DiarizationPythonEnvironment)
                : new DiarizationRuntimeInstallResult(false, $"diarize installed but import check failed: {status.Detail}", paths.DiarizationPythonEnvironment);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new DiarizationRuntimeInstallResult(false, $"diarize install failed: {exception.Message}", paths.DiarizationPythonEnvironment);
        }
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
            return ManagedPythonRuntimeResult.Failed(source.Message);
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
            return ManagedPythonRuntimeResult.Failed($"diarization Python venv creation failed: {error}");
        }

        var managed = await _pythonRuntimeResolver.ResolveInstalledRuntimeAsync(cancellationToken);
        return managed.IsFound && managed.Command is not null
            ? ManagedPythonRuntimeResult.Succeeded(managed.Command)
            : ManagedPythonRuntimeResult.Failed(managed.Message);
    }
}

public sealed record DiarizationRuntimeStatus(bool IsAvailable, string Detail, string InstallPath);

public sealed record DiarizationRuntimeInstallResult(bool IsSucceeded, string Message, string InstallPath);

internal sealed record ManagedPythonRuntimeResult(
    bool IsSucceeded,
    PythonRuntimeCommand? Command,
    string Message)
{
    public static ManagedPythonRuntimeResult Succeeded(PythonRuntimeCommand command)
    {
        return new ManagedPythonRuntimeResult(true, command, string.Empty);
    }

    public static ManagedPythonRuntimeResult Failed(string message)
    {
        return new ManagedPythonRuntimeResult(false, null, message);
    }
}
