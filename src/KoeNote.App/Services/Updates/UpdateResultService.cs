using System.IO;
using System.Text.Json;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateInstallerResult(
    string ResultPath,
    string Status,
    int ExitCode,
    string Version,
    string InstallerPath,
    string TargetExePath,
    string LogPath,
    DateTimeOffset CompletedAt,
    string Message);

public interface IUpdateResultService
{
    UpdateInstallerResult? ConsumeLatestResult();
}

public sealed class UpdateResultService(AppPaths paths) : IUpdateResultService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public UpdateInstallerResult? ConsumeLatestResult()
    {
        if (!Directory.Exists(paths.UpdateDownloads))
        {
            return null;
        }

        var resultPaths = Directory
            .EnumerateFiles(paths.UpdateDownloads, "*.result.json", SearchOption.TopDirectoryOnly)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var resultPath in resultPaths)
        {
            if (TryRead(resultPath) is not { } result)
            {
                MarkSeen(resultPath, ".invalid");
                continue;
            }

            MarkSeen(resultPath, ".seen");
            MarkRemainingSeen(resultPaths, resultPath);
            return result;
        }

        return null;
    }

    private static void MarkRemainingSeen(IReadOnlyList<string> resultPaths, string consumedPath)
    {
        foreach (var resultPath in resultPaths)
        {
            if (string.Equals(resultPath, consumedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MarkSeen(resultPath, ".seen");
        }
    }

    private static UpdateInstallerResult? TryRead(string resultPath)
    {
        try
        {
            var document = JsonSerializer.Deserialize<UpdateInstallerResultDocument>(
                File.ReadAllText(resultPath),
                JsonOptions);
            return document is null
                ? null
                : new UpdateInstallerResult(
                    resultPath,
                    document.Status ?? string.Empty,
                    document.ExitCode,
                    document.Version ?? string.Empty,
                    document.InstallerPath ?? string.Empty,
                    document.TargetExePath ?? string.Empty,
                    document.LogPath ?? string.Empty,
                    document.CompletedAt,
                    document.Message ?? string.Empty);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static void MarkSeen(string resultPath, string suffix)
    {
        try
        {
            File.Move(resultPath, resultPath + suffix, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record UpdateInstallerResultDocument(
        string? Status,
        int ExitCode,
        string? Version,
        string? InstallerPath,
        string? TargetExePath,
        string? LogPath,
        DateTimeOffset CompletedAt,
        string? Message);
}

public sealed class NullUpdateResultService : IUpdateResultService
{
    public static NullUpdateResultService Instance { get; } = new();

    private NullUpdateResultService()
    {
    }

    public UpdateInstallerResult? ConsumeLatestResult() => null;
}
