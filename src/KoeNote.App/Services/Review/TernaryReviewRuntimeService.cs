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

    public async Task<TernaryReviewRuntimeInstallResult> InstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstalled())
        {
            return new TernaryReviewRuntimeInstallResult(true, "Ternary review runtime is already installed.", paths.TernaryLlamaCompletionPath, string.Empty);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"koenote-ternary-runtime-{Guid.NewGuid():N}.zip");
        try
        {
            using (var response = await httpClient.GetAsync(RuntimeUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var destination = File.Create(tempPath);
                await source.CopyToAsync(destination, cancellationToken);
            }

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
}

public sealed record TernaryReviewRuntimeInstallResult(
    bool IsSucceeded,
    string Message,
    string InstallPath,
    string FailureCategory);
