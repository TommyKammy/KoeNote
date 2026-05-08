using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using KoeNote.App.Models;
using KoeNote.App.Services.Diagnostics;
using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Services.Export;

public sealed class JobLogExportService(AppPaths paths, JobLogRepository jobLogRepository)
{
    private readonly CrashLogService _crashLogService = new(paths);

    public void ExportDiagnosticReport(JobSummary job, string outputPath, DiagnosticLogScope scope = DiagnosticLogScope.SelectedJob)
    {
        ArgumentNullException.ThrowIfNull(job);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(outputPath, BuildDiagnosticReport(job, scope), Encoding.UTF8);
    }

    public void ExportDiagnosticPackage(JobSummary job, string outputPath, DiagnosticLogScope scope = DiagnosticLogScope.SelectedJob)
    {
        ArgumentNullException.ThrowIfNull(job);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        AddTextEntry(archive, "diagnostic-report.txt", BuildDiagnosticReport(job, scope));

        foreach (var file in EnumeratePackageFiles(job.JobId))
        {
            if (!File.Exists(file.SourcePath))
            {
                continue;
            }

            archive.CreateEntryFromFile(file.SourcePath, file.EntryName, CompressionLevel.Optimal);
        }
    }

    public string BuildDiagnosticReport(JobSummary job, DiagnosticLogScope scope = DiagnosticLogScope.SelectedJob)
    {
        ArgumentNullException.ThrowIfNull(job);

        var builder = new StringBuilder();
        AppendHeader(builder, "KoeNote Diagnostic Log");
        AppendKeyValue(builder, "GeneratedAt", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        AppendKeyValue(builder, "AppVersion", GetAppVersion());
        AppendKeyValue(builder, "OS", RuntimeInformation.OSDescription);
        AppendKeyValue(builder, ".NET", RuntimeInformation.FrameworkDescription);
        AppendKeyValue(builder, "ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
        AppendKeyValue(builder, "BaseDirectory", AppContext.BaseDirectory);
        AppendKeyValue(builder, "DataRoot", paths.Root);
        AppendKeyValue(builder, "LocalLogs", paths.Logs);
        AppendKeyValue(builder, "LogScope", FormatScope(scope));

        AppendHeader(builder, "Selected Job");
        AppendKeyValue(builder, "JobId", job.JobId);
        AppendKeyValue(builder, "Title", job.Title);
        AppendKeyValue(builder, "FileName", job.FileName);
        AppendKeyValue(builder, "Status", job.Status);
        AppendKeyValue(builder, "ProgressPercent", job.ProgressPercent.ToString(CultureInfo.InvariantCulture));
        AppendKeyValue(builder, "UnreviewedDrafts", job.UnreviewedDrafts.ToString(CultureInfo.InvariantCulture));
        AppendKeyValue(builder, "CreatedAt", job.CreatedAt.ToString("o", CultureInfo.InvariantCulture));
        AppendKeyValue(builder, "UpdatedAt", job.UpdatedAt.ToString("o", CultureInfo.InvariantCulture));
        AppendKeyValue(builder, "SourceAudioPath", job.SourceAudioPath);
        AppendKeyValue(builder, "NormalizedAudioPath", job.NormalizedAudioPath ?? "");

        AppendHeader(builder, "Job Log Events");
        var entries = jobLogRepository.ReadForDiagnostics(scope == DiagnosticLogScope.SelectedJob ? job.JobId : null);
        if (entries.Count == 0)
        {
            builder.AppendLine("(no log events)");
        }
        else
        {
            foreach (var entry in entries)
            {
                builder
                    .Append(entry.CreatedAt.ToString("o", CultureInfo.InvariantCulture))
                    .Append('\t')
                    .Append(entry.JobId ?? "-")
                    .Append('\t')
                    .Append(entry.Level)
                    .Append('\t')
                    .Append(entry.Stage)
                    .Append('\t')
                    .AppendLine(entry.Message);
            }
        }

        AppendHeader(builder, "Packaged Files");
        var packagedFiles = EnumeratePackageFiles(job.JobId).ToArray();
        if (packagedFiles.Length == 0)
        {
            builder.AppendLine("(no packaged files found)");
        }
        else
        {
            foreach (var file in packagedFiles)
            {
                builder.Append(file.EntryName).Append('\t').AppendLine(file.SourcePath);
            }
        }

        AppendHeader(builder, "Recent Crash Logs");
        var crashLogs = _crashLogService.ReadRecentCrashLogs();
        if (crashLogs.Count == 0)
        {
            builder.AppendLine("(no crash logs found)");
        }
        else
        {
            foreach (var crashLog in crashLogs)
            {
                builder.AppendLine("### " + crashLog.Path);
                builder.AppendLine(crashLog.Content.TrimEnd());
                builder.AppendLine();
            }
        }

        builder.AppendLine();
        builder.AppendLine("Note: This report lists packaged diagnostic log paths, job events, and recent crash logs. Raw transcript, prompt, and worker-output contents are not embedded.");
        return builder.ToString();
    }

    private IEnumerable<DiagnosticPackageFile> EnumeratePackageFiles(string jobId)
    {
        var jobLogDirectory = Path.Combine(paths.Jobs, jobId, "logs");
        if (Directory.Exists(jobLogDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(jobLogDirectory, "*.log", SearchOption.AllDirectories)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                yield return new DiagnosticPackageFile(
                    file,
                    CreateZipEntryName("job-logs", Path.GetRelativePath(jobLogDirectory, file)));
            }
        }

        if (Directory.Exists(paths.Logs))
        {
            foreach (var file in Directory.EnumerateFiles(paths.Logs, "crash-*.log", SearchOption.TopDirectoryOnly)
                         .Order(StringComparer.OrdinalIgnoreCase))
            {
                yield return new DiagnosticPackageFile(
                    file,
                    CreateZipEntryName("crash-logs", Path.GetFileName(file)));
            }
        }
    }

    private static void AddTextEntry(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.Write(content);
    }

    private static string CreateZipEntryName(string prefix, string relativePath)
    {
        return prefix + "/" + relativePath.Replace('\\', '/');
    }

    private static void AppendHeader(StringBuilder builder, string text)
    {
        builder.AppendLine();
        builder.AppendLine("## " + text);
    }

    private static void AppendKeyValue(StringBuilder builder, string key, string value)
    {
        builder.Append(key).Append(": ").AppendLine(value);
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(JobLogExportService).Assembly;
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }

    private static string FormatScope(DiagnosticLogScope scope)
    {
        return scope switch
        {
            DiagnosticLogScope.SelectedJob => "selected-job",
            DiagnosticLogScope.RecentAllJobs => "recent-all-jobs",
            _ => scope.ToString()
        };
    }

    private sealed record DiagnosticPackageFile(string SourcePath, string EntryName);
}
