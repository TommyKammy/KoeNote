using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace KoeNote.App.Services.Setup;

public interface ISetupHostResourceProbe
{
    SetupHostResources GetResources();
}

internal sealed class WindowsSetupHostResourceProbe : ISetupHostResourceProbe
{
    public SetupHostResources GetResources()
    {
        var totalMemoryBytes = TryGetTotalMemoryBytes();
        var maxGpuMemoryGb = TryGetMaxNvidiaGpuMemoryGb();
        var logicalProcessorCount = Environment.ProcessorCount > 0 ? Environment.ProcessorCount : (int?)null;
        var summary = FormatSummary(totalMemoryBytes, maxGpuMemoryGb, logicalProcessorCount);
        return new SetupHostResources(
            totalMemoryBytes,
            maxGpuMemoryGb,
            NvidiaGpuDetected: maxGpuMemoryGb is not null,
            logicalProcessorCount,
            summary);
    }

    private static long? TryGetTotalMemoryBytes()
    {
        var status = new MemoryStatusEx();
        status.dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>();
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : null;
    }

    private static int? TryGetMaxNvidiaGpuMemoryGb()
    {
        var nvidiaSmi = ResolveCommand("nvidia-smi");
        if (nvidiaSmi is null)
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = nvidiaSmi,
                Arguments = "--query-gpu=memory.total --format=csv,noheader,nounits",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return null;
            }

            if (!process.WaitForExit(2500))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            var memoryGb = process.StandardOutput.ReadToEnd()
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static line => int.TryParse(line, out var memoryMiB) ? (int?)Math.Max(1, (int)Math.Ceiling(memoryMiB / 1024d)) : null)
                .Where(static value => value is not null)
                .Select(static value => value!.Value)
                .DefaultIfEmpty()
                .Max();
            return memoryGb > 0 ? memoryGb : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveCommand(string commandName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var path in paths)
        {
            var executable = commandName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? commandName : $"{commandName}.exe";
            var candidate = Path.Combine(path, executable);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string FormatSummary(long? totalMemoryBytes, int? maxGpuMemoryGb, int? logicalProcessorCount)
    {
        var memorySummary = totalMemoryBytes is { } bytes
            ? $"RAM {Math.Max(1, (int)Math.Round(bytes / 1024d / 1024d / 1024d))}GB"
            : "RAM 不明";
        var cpuSummary = logicalProcessorCount is { } cores
            ? $"CPU {cores}論理コア"
            : "CPU 不明";
        var gpuSummary = maxGpuMemoryGb is { } vramGb
            ? $"NVIDIA GPU VRAM {vramGb}GB"
            : "NVIDIA GPU 未検出";
        return $"{memorySummary} / {cpuSummary} / {gpuSummary}";
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }
}
