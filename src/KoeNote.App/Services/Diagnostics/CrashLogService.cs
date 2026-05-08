using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace KoeNote.App.Services.Diagnostics;

public sealed class CrashLogService(AppPaths paths)
{
    public string WriteAppStartLog()
    {
        var path = CreateLogPath("app-start");
        WriteLog(path, "AppStart", null);
        return path;
    }

    public string WriteExceptionLog(string source, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var path = CreateLogPath("crash");
        WriteLog(path, source, exception);
        return path;
    }

    public IReadOnlyList<CrashLogFile> ReadRecentCrashLogs(int limit = 3)
    {
        if (!Directory.Exists(paths.Logs))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(paths.Logs, "crash-*.log", SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(Math.Max(0, limit))
            .Select(file => new CrashLogFile(file.FullName, ReadLogContent(file.FullName)))
            .ToArray();
    }

    private string CreateLogPath(string prefix)
    {
        Directory.CreateDirectory(paths.Logs);
        return Path.Combine(paths.Logs, $"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss-fff}.log");
    }

    private void WriteLog(string path, string source, Exception? exception)
    {
        var builder = new StringBuilder();
        builder.AppendLine("## KoeNote Runtime Log");
        builder.Append("CreatedAt: ").AppendLine(DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture));
        builder.Append("Source: ").AppendLine(source);
        builder.Append("AppVersion: ").AppendLine(GetAppVersion());
        builder.Append("OS: ").AppendLine(RuntimeInformation.OSDescription);
        builder.Append(".NET: ").AppendLine(RuntimeInformation.FrameworkDescription);
        builder.Append("ProcessArchitecture: ").AppendLine(RuntimeInformation.ProcessArchitecture.ToString());
        builder.Append("BaseDirectory: ").AppendLine(AppContext.BaseDirectory);
        builder.Append("DataRoot: ").AppendLine(paths.Root);
        builder.Append("LocalLogs: ").AppendLine(paths.Logs);

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append("ExceptionType: ").AppendLine(exception.GetType().FullName);
            builder.Append("Message: ").AppendLine(exception.Message);
            builder.AppendLine();
            builder.AppendLine("Exception:");
            builder.AppendLine(exception.ToString());
        }

        File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
    }

    private static string ReadLogContent(string path)
    {
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"(failed to read crash log: {exception.Message})";
        }
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(CrashLogService).Assembly;
        return assembly.GetName().Version?.ToString() ?? "unknown";
    }
}

public sealed record CrashLogFile(string Path, string Content);
