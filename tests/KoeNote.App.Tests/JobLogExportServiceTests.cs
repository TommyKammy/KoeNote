using KoeNote.App.Models;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using System.IO.Compression;

namespace KoeNote.App.Tests;

public sealed class JobLogExportServiceTests
{
    [Fact]
    public void BuildDiagnosticReport_IncludesEnvironmentJobLogsAndRelatedFiles()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new JobLogRepository(paths);
        var service = new JobLogExportService(paths, repository);
        var job = CreateJob();
        var relatedFile = Path.Combine(paths.Jobs, job.JobId, "logs", "preprocess.log");
        Directory.CreateDirectory(Path.GetDirectoryName(relatedFile)!);
        File.WriteAllText(relatedFile, "worker output");
        repository.AddEvent(job.JobId, "created", "info", "Registered audio file");
        repository.AddEvent(job.JobId, "asr", "error", "ProcessFailed: ASR failed");
        repository.AddEvent("other-job", "asr", "info", "not included");
        var crashLog = Path.Combine(paths.Logs, "crash-20260508-120000-000.log");
        Directory.CreateDirectory(paths.Logs);
        File.WriteAllText(crashLog, "crash details");

        var report = service.BuildDiagnosticReport(job);

        Assert.Contains("## KoeNote Diagnostic Log", report);
        Assert.Contains("AppVersion:", report);
        Assert.Contains("OS:", report);
        Assert.Contains("JobId: job-001", report);
        Assert.Contains("FileName: meeting.wav", report);
        Assert.Contains("LogScope: selected-job", report);
        Assert.Contains("Registered audio file", report);
        Assert.Contains("ProcessFailed: ASR failed", report);
        Assert.DoesNotContain("not included", report);
        Assert.Contains(relatedFile, report);
        Assert.Contains("job-logs/preprocess.log", report);
        Assert.Contains("## Recent Crash Logs", report);
        Assert.Contains(crashLog, report);
        Assert.Contains("crash details", report);
        Assert.Contains("Raw transcript, prompt, and worker-output contents are not embedded.", report);
    }

    [Fact]
    public void BuildDiagnosticReport_WithRecentAllJobsScope_IncludesRecentEventsFromOtherJobs()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new JobLogRepository(paths);
        var service = new JobLogExportService(paths, repository);
        var job = CreateJob();
        repository.AddEvent(job.JobId, "created", "info", "selected job event");
        repository.AddEvent("other-job", "asr", "error", "other job event");

        var report = service.BuildDiagnosticReport(job, DiagnosticLogScope.RecentAllJobs);

        Assert.Contains("LogScope: recent-all-jobs", report);
        Assert.Contains("selected job event", report);
        Assert.Contains("other-job", report);
        Assert.Contains("other job event", report);
    }

    [Fact]
    public void ExportDiagnosticReport_WritesUtf8TextFile()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var service = new JobLogExportService(paths, new JobLogRepository(paths));
        var outputPath = Path.Combine(paths.Root, "exports", "diagnostic.txt");

        service.ExportDiagnosticReport(CreateJob(), outputPath);

        Assert.True(File.Exists(outputPath));
        Assert.Contains("KoeNote Diagnostic Log", File.ReadAllText(outputPath));
    }

    [Fact]
    public void ExportDiagnosticPackage_IncludesReportAndSafeLogsOnly()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var service = new JobLogExportService(paths, new JobLogRepository(paths));
        var job = CreateJob();
        var jobLogPath = Path.Combine(paths.Jobs, job.JobId, "logs", "preprocess.log");
        var asrWorkerLogPath = Path.Combine(paths.Jobs, job.JobId, "logs", "asr-20260512145447000-abcdef.log");
        var excludedFiles = new[]
        {
            Path.Combine(paths.Jobs, job.JobId, "asr", "asr.raw.json"),
            Path.Combine(paths.Jobs, job.JobId, "diarization", "diarize.raw.json"),
            Path.Combine(paths.Jobs, job.JobId, "review", "review.chunk-001.raw.json"),
            Path.Combine(paths.Jobs, job.JobId, "summary", "summary.chunk-001.raw.md")
        };
        Directory.CreateDirectory(Path.GetDirectoryName(jobLogPath)!);
        File.WriteAllText(jobLogPath, "ffmpeg log");
        File.WriteAllText(asrWorkerLogPath, "asr worker diagnostics");
        foreach (var file in excludedFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, "sensitive content");
        }

        var crashLogPath = Path.Combine(paths.Logs, "crash-20260508-120000-000.log");
        Directory.CreateDirectory(paths.Logs);
        File.WriteAllText(crashLogPath, "crash log");
        var outputPath = Path.Combine(paths.Root, "exports", "diagnostic.zip");

        service.ExportDiagnosticPackage(job, outputPath);

        using var archive = ZipFile.OpenRead(outputPath);
        var entries = archive.Entries.Select(static entry => entry.FullName).ToArray();
        Assert.Contains("diagnostic-report.txt", entries);
        Assert.Contains("job-logs/preprocess.log", entries);
        Assert.Contains("job-logs/asr-20260512145447000-abcdef.log", entries);
        Assert.Contains("crash-logs/crash-20260508-120000-000.log", entries);
        Assert.DoesNotContain(entries, entry => entry.Contains("asr.raw.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.Contains("diarize.raw.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".raw.json", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.EndsWith(".raw.md", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.Contains("prompt", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(entries, entry => entry.Contains("segments.normalized", StringComparison.OrdinalIgnoreCase));

        var reportEntry = archive.GetEntry("diagnostic-report.txt");
        Assert.NotNull(reportEntry);
        using var reportReader = new StreamReader(reportEntry.Open());
        var report = reportReader.ReadToEnd();
        Assert.DoesNotContain("asr.raw.json", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("diarize.raw.json", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".raw.md", report, StringComparison.OrdinalIgnoreCase);
    }

    private static JobSummary CreateJob()
    {
        var now = DateTimeOffset.Parse("2026-05-08T12:34:56+09:00");
        return new JobSummary(
            "job-001",
            "Meeting",
            "meeting.wav",
            @"C:\Audio\meeting.wav",
            "asr_failed",
            60,
            0,
            now,
            now.AddMinutes(-5),
            @"C:\KoeNote\jobs\job-001\normalized\audio.wav");
    }
}
