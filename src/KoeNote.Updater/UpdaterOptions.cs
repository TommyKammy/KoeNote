namespace KoeNote.Updater;

public sealed record UpdaterOptions(
    string MsiPath,
    string ExpectedSha256,
    string TargetExePath,
    string InstallFolderPath,
    int ParentProcessId,
    string LogPath,
    string ResultPath,
    string Version,
    int ParentExitTimeoutSeconds = 120)
{
    public static string HelpText => """
        Usage:
          KoeNote.Updater.exe --msi <path> --sha256 <hex> --target-exe <path> --install-folder <path> --parent-pid <pid> --log <path> --version <version> [--result <path>] [--parent-timeout-seconds <seconds>]
        """;

    public static UpdaterOptions Parse(IReadOnlyList<string> args)
    {
        var values = ReadValues(args);
        var msiPath = Require(values, "--msi");
        var sha256 = Require(values, "--sha256");
        var targetExePath = Require(values, "--target-exe");
        var installFolderPath = values.TryGetValue("--install-folder", out var explicitInstallFolderPath) &&
            !string.IsNullOrWhiteSpace(explicitInstallFolderPath)
            ? explicitInstallFolderPath
            : Path.GetDirectoryName(Path.GetFullPath(targetExePath))
                ?? throw new ArgumentException("--install-folder is required when --target-exe has no directory.");
        var parentPidValue = Require(values, "--parent-pid");
        var logPath = Require(values, "--log");
        var version = Require(values, "--version");
        var resultPath = values.TryGetValue("--result", out var explicitResultPath) && !string.IsNullOrWhiteSpace(explicitResultPath)
            ? explicitResultPath
            : Path.ChangeExtension(logPath, ".result.json");
        var parentExitTimeoutSeconds = values.TryGetValue("--parent-timeout-seconds", out var timeoutValue)
            ? timeoutValue
            : "120";

        if (!int.TryParse(parentPidValue, out var parentPid) || parentPid < 0)
        {
            throw new ArgumentException("--parent-pid must be a non-negative integer.");
        }

        if (!int.TryParse(parentExitTimeoutSeconds, out var parentTimeoutSeconds) || parentTimeoutSeconds <= 0)
        {
            throw new ArgumentException("--parent-timeout-seconds must be a positive integer.");
        }

        if (!IsHexSha256(sha256))
        {
            throw new ArgumentException("--sha256 must be a 64-character hexadecimal SHA256 value.");
        }

        return new UpdaterOptions(
            Path.GetFullPath(msiPath),
            sha256.ToLowerInvariant(),
            Path.GetFullPath(targetExePath),
            Path.GetFullPath(installFolderPath),
            parentPid,
            Path.GetFullPath(logPath),
            Path.GetFullPath(resultPath),
            version,
            parentTimeoutSeconds);
    }

    private static Dictionary<string, string> ReadValues(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Count; index++)
        {
            var option = args[index];
            if (!option.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unexpected argument: {option}");
            }

            var valueIndex = index + 1;
            if (valueIndex >= args.Count || args[valueIndex].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            values[option] = args[valueIndex];
            index = valueIndex;
        }

        return values;
    }

    private static string Require(IReadOnlyDictionary<string, string> values, string option)
    {
        if (!values.TryGetValue(option, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{option} is required.");
        }

        return value;
    }

    private static bool IsHexSha256(string value)
    {
        return value.Length == 64 && value.All(static character =>
            character is >= '0' and <= '9' ||
            character is >= 'a' and <= 'f' ||
            character is >= 'A' and <= 'F');
    }
}
