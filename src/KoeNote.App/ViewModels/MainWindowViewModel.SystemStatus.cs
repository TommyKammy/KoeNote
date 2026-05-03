using System.Diagnostics;
using System.IO;

namespace KoeNote.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private string GetDiskFreeSummary()
    {
        try
        {
            var root = Path.GetPathRoot(Paths.Root);
            if (string.IsNullOrWhiteSpace(root))
            {
                return "空き容量 Unknown";
            }

            var drive = new DriveInfo(root);
            return $"空き容量 {drive.AvailableFreeSpace / 1024d / 1024d / 1024d:N1} GB";
        }
        catch (IOException)
        {
            return "空き容量 Unknown";
        }
        catch (UnauthorizedAccessException)
        {
            return "空き容量 Unknown";
        }
    }

    private static string GetMemorySummary()
    {
        using var process = Process.GetCurrentProcess();
        return $"MEM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

    private static string GetCpuSummary()
    {
        using var process = Process.GetCurrentProcess();
        var uptime = DateTimeOffset.Now - process.StartTime;
        if (uptime <= TimeSpan.Zero)
        {
            return "CPU Unknown";
        }

        var averageUsage = process.TotalProcessorTime.TotalMilliseconds / uptime.TotalMilliseconds / Environment.ProcessorCount * 100;
        return $"CPU {Math.Clamp(averageUsage, 0, 100):N0}%";
    }

    private static string GetGpuUsageSummary()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=utilization.gpu,memory.used --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return "GPU Unknown";
            }

            if (!process.WaitForExit(1200))
            {
                process.Kill(entireProcessTree: true);
                return "GPU Unknown";
            }

            var firstLine = process.StandardOutput.ReadToEnd()
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault();
            if (string.IsNullOrWhiteSpace(firstLine))
            {
                return "GPU Unknown";
            }

            var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                return $"GPU {firstLine}";
            }

            return $"GPU {parts[0]}% / {parts[1]} MB";
        }
        catch
        {
            return "GPU Unknown";
        }
    }
}
