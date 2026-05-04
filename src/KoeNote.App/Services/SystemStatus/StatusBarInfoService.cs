using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace KoeNote.App.Services.SystemStatus;

public sealed class StatusBarInfoService(AppPaths paths)
{
    private ulong _lastIdleTime;
    private ulong _lastKernelTime;
    private ulong _lastUserTime;
    private bool _hasCpuSample;

    public StatusBarInfo GetStatusBarInfo()
    {
        return new StatusBarInfo(
            GetDiskFreeSummary(),
            GetMemorySummary(),
            GetCpuSummary(),
            GetGpuUsageSummary());
    }

    public static string FormatGpuUsage(string? output)
    {
        var firstLine = output?
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return "GPU Unknown";
        }

        var parts = firstLine.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return $"GPU {firstLine}";
        }

        return $"GPU {parts[0]}% / {parts[1]} MB";
    }

    private string GetDiskFreeSummary()
    {
        try
        {
            var root = Path.GetPathRoot(paths.Root);
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
        catch (ArgumentException)
        {
            return "空き容量 Unknown";
        }
    }

    private static string GetMemorySummary()
    {
        using var process = Process.GetCurrentProcess();
        return $"MEM {process.WorkingSet64 / 1024 / 1024:N0} MB";
    }

    private string GetCpuSummary()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return "CPU Unknown";
        }

        var idle = idleTime.ToUInt64();
        var kernel = kernelTime.ToUInt64();
        var user = userTime.ToUInt64();

        if (!_hasCpuSample)
        {
            _lastIdleTime = idle;
            _lastKernelTime = kernel;
            _lastUserTime = user;
            _hasCpuSample = true;
            return "CPU --%";
        }

        var idleDiff = idle - _lastIdleTime;
        var kernelDiff = kernel - _lastKernelTime;
        var userDiff = user - _lastUserTime;
        var total = kernelDiff + userDiff;

        _lastIdleTime = idle;
        _lastKernelTime = kernel;
        _lastUserTime = user;

        if (total == 0)
        {
            return "CPU 0%";
        }

        var usage = (1 - idleDiff / (double)total) * 100;
        return $"CPU {Math.Clamp(usage, 0, 100):N0}%";
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

            return FormatGpuUsage(process.StandardOutput.ReadToEnd());
        }
        catch
        {
            return "GPU Unknown";
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idleTime, out FileTime kernelTime, out FileTime userTime);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct FileTime
    {
        private readonly uint _lowDateTime;
        private readonly uint _highDateTime;

        public ulong ToUInt64()
        {
            return ((ulong)_highDateTime << 32) | _lowDateTime;
        }
    }
}
