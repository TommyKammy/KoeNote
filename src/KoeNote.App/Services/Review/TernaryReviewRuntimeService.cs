using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace KoeNote.App.Services.Review;

public sealed class TernaryReviewRuntimeService(AppPaths paths, HttpClient httpClient)
{
    public const string RuntimeUrl = "https://github.com/PrismML-Eng/llama.cpp/releases/download/prism-b8846-d104cf1/llama-bin-win-cpu-x64.zip";
    public const string FailureCategoryNetworkUnavailable = "NetworkUnavailable";
    public const string FailureCategoryArchiveInvalid = "ArchiveInvalid";
    public const string FailureCategoryInstallFailed = "InstallFailed";

    public bool IsInstalled()
    {
        return File.Exists(paths.TernaryLlamaCompletionPath);
    }

    public async Task<TernaryReviewRuntimeInstallResult> InstallAsync(
        CancellationToken cancellationToken = default,
        IProgress<RuntimeInstallProgress>? progress = null)
    {
        if (IsInstalled())
        {
            return new TernaryReviewRuntimeInstallResult(true, "Ternary review runtime is already installed.", paths.TernaryLlamaCompletionPath, string.Empty);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-ternary-runtime-{Guid.NewGuid():N}.zip");
        try
        {
            await DownloadAsync(tempPath, cancellationToken, progress);

            Report(progress, "検証中", "Ternary review runtime archive を検証しています...", 85);
            var installRoot = Path.GetDirectoryName(paths.TernaryLlamaCompletionPath)!;
            Directory.CreateDirectory(installRoot);
            using var archive = ZipFile.OpenRead(tempPath);
            var completionEntry = archive.Entries.FirstOrDefault(static entry =>
                entry.FullName.Equals("llama-completion.exe", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith("/llama-completion.exe", StringComparison.OrdinalIgnoreCase) ||
                entry.FullName.EndsWith("\\llama-completion.exe", StringComparison.OrdinalIgnoreCase));
            if (completionEntry is null)
            {
                return new TernaryReviewRuntimeInstallResult(false, "Ternary review runtime archive did not contain llama-completion.exe.", installRoot, FailureCategoryArchiveInvalid);
            }

            Report(progress, "展開中", "Ternary review runtime を展開しています...", 95);
            ExtractArchive(archive, installRoot);
            return IsInstalled()
                ? new TernaryReviewRuntimeInstallResult(true, $"Ternary review runtime installed: {paths.TernaryLlamaCompletionPath}", paths.TernaryLlamaCompletionPath, string.Empty)
                : new TernaryReviewRuntimeInstallResult(false, $"Ternary review runtime was extracted, but llama-completion.exe was not found: {paths.TernaryLlamaCompletionPath}", installRoot, FailureCategoryInstallFailed);
        }
        catch (HttpRequestException exception)
        {
            return new TernaryReviewRuntimeInstallResult(false, $"Ternary review runtime download failed: {exception.Message}", paths.TernaryLlamaCompletionPath, FailureCategoryNetworkUnavailable);
        }
        catch (InvalidDataException exception)
        {
            return new TernaryReviewRuntimeInstallResult(false, $"Ternary review runtime archive could not be read: {exception.Message}", paths.TernaryLlamaCompletionPath, FailureCategoryArchiveInvalid);
        }
        catch (IOException exception)
        {
            return new TernaryReviewRuntimeInstallResult(false, $"Ternary review runtime install failed: {exception.Message}", paths.TernaryLlamaCompletionPath, FailureCategoryInstallFailed);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private async Task DownloadAsync(
        string tempPath,
        CancellationToken cancellationToken,
        IProgress<RuntimeInstallProgress>? progress)
    {
        using var response = await httpClient.GetAsync(RuntimeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = File.Create(tempPath);
        await CopyToAsync(
            source,
            destination,
            totalBytes,
            "ダウンロード中",
            "Ternary review runtime をダウンロードしています...",
            progress,
            80,
            cancellationToken);
    }

    private static async Task CopyToAsync(
        Stream source,
        Stream destination,
        long? totalBytes,
        string stageText,
        string message,
        IProgress<RuntimeInstallProgress>? progress,
        double completePercent,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long downloadedBytes = 0;
        var reporter = new RuntimeInstallProgressReporter(progress);
        Report(reporter, stageText, message, downloadedBytes, totalBytes, completePercent, force: true);

        while (true)
        {
            var bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            downloadedBytes += bytesRead;
            Report(reporter, stageText, message, downloadedBytes, totalBytes, completePercent);
        }

        Report(reporter, stageText, message, downloadedBytes, totalBytes, completePercent, force: true);
    }

    private static void ExtractArchive(ZipArchive archive, string installRoot)
    {
        var root = Path.GetFullPath(installRoot);
        foreach (var entry in archive.Entries.Where(static entry => !string.IsNullOrWhiteSpace(entry.Name)))
        {
            var destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Archive entry escapes the install directory: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static void Report(
        IProgress<RuntimeInstallProgress>? progress,
        string stageText,
        string message,
        double percent)
    {
        progress?.Report(new RuntimeInstallProgress(stageText, message, percent, IsIndeterminate: false));
    }

    private static void Report(
        RuntimeInstallProgressReporter reporter,
        string stageText,
        string message,
        long bytesDownloaded,
        long? bytesTotal,
        double completePercent,
        bool force = false)
    {
        var percent = bytesTotal is > 0 && bytesDownloaded <= bytesTotal.Value
            ? Math.Clamp(bytesDownloaded * completePercent / bytesTotal.Value, 0, completePercent)
            : (double?)null;
        reporter.Report(new RuntimeInstallProgress(
            stageText,
            message,
            percent,
            BytesDownloaded: bytesDownloaded,
            BytesTotal: bytesTotal,
            IsIndeterminate: !bytesTotal.HasValue),
            force);
    }
}

public sealed record TernaryReviewRuntimeInstallResult(
    bool IsSucceeded,
    string Message,
    string InstallPath,
    string FailureCategory);
