using System.Globalization;
using System.IO;

namespace KoeNote.App.Services.Diarization;

public sealed class PythonRuntimeResolver(
    AppPaths paths,
    ExternalProcessRunner processRunner)
{
    public static readonly Version MinimumSupportedVersion = new(3, 9);
    public static readonly Version MaximumSupportedVersionExclusive = new(3, 13);

    private static readonly IReadOnlyList<PythonRuntimeCandidate> DiscoveryCandidates =
    [
        new("py", ["-3.12"], "Python launcher 3.12"),
        new("py", ["-3.11"], "Python launcher 3.11"),
        new("py", ["-3.10"], "Python launcher 3.10"),
        new("py", ["-3.9"], "Python launcher 3.9"),
        new("python3.12", [], "python3.12"),
        new("python3.11", [], "python3.11"),
        new("python3.10", [], "python3.10"),
        new("python3.9", [], "python3.9"),
        new("python", [], "python")
    ];

    public async Task<PythonRuntimeResolveResult> ResolveInstalledRuntimeAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(paths.DiarizationPythonPath))
        {
            var managed = await ProbeAsync(
                new PythonRuntimeCandidate(paths.DiarizationPythonPath, [], "KoeNote diarization Python"),
                cancellationToken);
            if (managed.IsCompatible)
            {
                return PythonRuntimeResolveResult.Found(managed.Command!);
            }

            return PythonRuntimeResolveResult.NotFound(BuildUnsupportedManagedRuntimeMessage(managed));
        }

        if (!DiarizationRuntimeLayout.HasLegacyPackage(paths))
        {
            return PythonRuntimeResolveResult.NotFound(
                $"The speaker diarization runtime is not installed. Install it from setup: {paths.DiarizationPythonEnvironment}");
        }

        var legacy = await ResolveCompatiblePythonAsync(cancellationToken);
        return legacy.Command is null
            ? PythonRuntimeResolveResult.NotFound(legacy.Message)
            : PythonRuntimeResolveResult.Found(legacy.Command with
            {
                Environment = PythonRuntimeEnvironment.BuildLegacy(paths),
                InstallPath = paths.PythonPackages
            });
    }

    public async Task<PythonRuntimeResolveResult> ResolveInstallSourceAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(paths.BundledPythonPath))
        {
            var bundled = await ProbeAsync(
                new PythonRuntimeCandidate(paths.BundledPythonPath, [], "bundled Python"),
                cancellationToken);
            if (bundled.IsCompatible)
            {
                return PythonRuntimeResolveResult.Found(bundled.Command!);
            }
        }

        return await ResolveCompatiblePythonAsync(cancellationToken);
    }

    public async Task<PythonRuntimeResolveResult> ResolveCompatiblePythonAsync(CancellationToken cancellationToken = default)
    {
        var failures = new List<string>();
        foreach (var candidate in DiscoveryCandidates)
        {
            var probe = await ProbeAsync(candidate, cancellationToken);
            if (probe.IsCompatible)
            {
                return PythonRuntimeResolveResult.Found(probe.Command!);
            }

            if (!string.IsNullOrWhiteSpace(probe.Detail))
            {
                failures.Add($"{candidate.DisplayName}: {probe.Detail}");
            }
        }

        return PythonRuntimeResolveResult.NotFound(BuildNoCompatiblePythonMessage(failures));
    }

    public static bool IsSupportedVersion(Version version)
    {
        return version >= MinimumSupportedVersion &&
            version < MaximumSupportedVersionExclusive;
    }

    private async Task<PythonRuntimeProbe> ProbeAsync(
        PythonRuntimeCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await processRunner.RunAsync(
                candidate.FileName,
                [
                    ..candidate.PrefixArguments,
                    "-c",
                    "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}|{sys.executable}')"
                ],
                TimeSpan.FromSeconds(10),
                cancellationToken);
            var output = string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError.Trim()
                : result.StandardOutput.Trim();
            if (result.ExitCode != 0)
            {
                return PythonRuntimeProbe.Failed(output);
            }

            var parsed = ParseVersionProbe(output);
            if (parsed is null)
            {
                return PythonRuntimeProbe.Failed($"unexpected version output: {output}");
            }

            var (version, executablePath) = parsed.Value;
            var command = new PythonRuntimeCommand(
                candidate.FileName,
                candidate.PrefixArguments,
                version,
                executablePath,
                candidate.DisplayName,
                Environment: null,
                InstallPath: paths.DiarizationPythonEnvironment);

            return IsSupportedVersion(version)
                ? PythonRuntimeProbe.Compatible(command)
                : PythonRuntimeProbe.Failed($"Python {version} is unsupported.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return PythonRuntimeProbe.Failed(exception.Message);
        }
    }

    private static (Version Version, string ExecutablePath)? ParseVersionProbe(string output)
    {
        var firstLine = output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return null;
        }

        var parts = firstLine.Split('|', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !Version.TryParse(parts[0], out var version))
        {
            return null;
        }

        return (version, parts[1]);
    }

    public static string BuildNoCompatiblePythonMessage(IReadOnlyList<string> failures)
    {
        var unsupportedRuntimes = failures
            .Select(NormalizeFailureDetail)
            .Where(detail => detail.Contains(" is unsupported.", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();
        var detected = unsupportedRuntimes.Length == 0
            ? string.Empty
            : $" Detected unsupported runtime: {string.Join("; ", unsupportedRuntimes)}";

        var notFound = failures.Count == 0
            ? " No Python executable was detected."
            : string.Empty;

        return string.Create(CultureInfo.InvariantCulture,
            $"No compatible Python runtime was found for speaker diarization. Install Python 3.12 x64 (or Python 3.11 x64), then retry. Supported versions are Python 3.9-3.12.{detected}{notFound}");
    }

    private static string NormalizeFailureDetail(string detail)
    {
        return detail
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? detail.Trim();
    }

    private static string BuildUnsupportedManagedRuntimeMessage(PythonRuntimeProbe probe)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"The KoeNote diarization Python runtime is not compatible anymore. Delete it and reinstall the diarization runtime. Details: {probe.Detail}");
    }
}

public sealed record PythonRuntimeCommand(
    string FileName,
    IReadOnlyList<string> PrefixArguments,
    Version Version,
    string ExecutablePath,
    string DisplayName,
    IReadOnlyDictionary<string, string>? Environment,
    string InstallPath)
{
    public IReadOnlyList<string> BuildArguments(params string[] arguments)
    {
        return [..PrefixArguments, ..arguments];
    }
}

public sealed record PythonRuntimeResolveResult(
    bool IsFound,
    PythonRuntimeCommand? Command,
    string Message)
{
    public static PythonRuntimeResolveResult Found(PythonRuntimeCommand command)
    {
        return new PythonRuntimeResolveResult(true, command, string.Empty);
    }

    public static PythonRuntimeResolveResult NotFound(string message)
    {
        return new PythonRuntimeResolveResult(false, null, message);
    }
}

internal sealed record PythonRuntimeCandidate(
    string FileName,
    IReadOnlyList<string> PrefixArguments,
    string DisplayName);

internal sealed record PythonRuntimeProbe(
    bool IsCompatible,
    PythonRuntimeCommand? Command,
    string Detail)
{
    public static PythonRuntimeProbe Compatible(PythonRuntimeCommand command)
    {
        return new PythonRuntimeProbe(true, command, string.Empty);
    }

    public static PythonRuntimeProbe Failed(string detail)
    {
        return new PythonRuntimeProbe(false, null, detail);
    }
}
