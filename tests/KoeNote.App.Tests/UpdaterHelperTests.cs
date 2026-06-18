using System.Security.Cryptography;
using System.Text.Json;
using KoeNote.Updater;

namespace KoeNote.App.Tests;

public sealed class UpdaterHelperTests
{
    [Fact]
    public async Task ExecuteAsync_WaitsForParentAndRunsSilentMsiThenRelaunches()
    {
        var root = CreateTempRoot();
        var msiPath = Path.Combine(root, "KoeNote.msi");
        var targetExe = Path.Combine(root, "KoeNote.App.exe");
        var logPath = Path.Combine(root, "update.log");
        var resultPath = Path.Combine(root, "update.result.json");
        await File.WriteAllTextAsync(msiPath, "installer");
        await File.WriteAllTextAsync(targetExe, "app");
        var options = new UpdaterOptions(msiPath, ComputeSha256("installer"), targetExe, 1234, logPath, resultPath, "0.20.0");
        var runner = new RecordingUpdaterProcessRunner();
        var service = new UpdaterService(runner);

        var exitCode = await service.ExecuteAsync(options);

        Assert.Equal(UpdaterExitCode.Success, exitCode);
        Assert.Equal(1234, runner.WaitedProcessIds.Single());
        var install = Assert.Single(runner.Runs);
        Assert.Equal("msiexec.exe", install.FileName);
        Assert.Equal(["/i", msiPath, "/qn", "/norestart", "/L*v", logPath], install.Arguments);
        Assert.Equal(targetExe, runner.Starts.Single());
        var result = ReadResult(resultPath);
        Assert.Equal((int)UpdaterExitCode.Success, result.ExitCode);
        Assert.Equal("0.20.0", result.Version);
    }

    [Fact]
    public async Task ExecuteAsync_RefusesTamperedMsiBeforeInstall()
    {
        var root = CreateTempRoot();
        var msiPath = Path.Combine(root, "KoeNote.msi");
        var targetExe = Path.Combine(root, "KoeNote.App.exe");
        var resultPath = Path.Combine(root, "update.result.json");
        await File.WriteAllTextAsync(msiPath, "tampered");
        var options = new UpdaterOptions(
            msiPath,
            ComputeSha256("original"),
            targetExe,
            0,
            Path.Combine(root, "update.log"),
            resultPath,
            "0.20.0");
        var runner = new RecordingUpdaterProcessRunner();
        var service = new UpdaterService(runner);

        var exitCode = await service.ExecuteAsync(options);

        Assert.Equal(UpdaterExitCode.VerificationFailed, exitCode);
        Assert.Empty(runner.Runs);
        Assert.Empty(runner.Starts);
        Assert.Equal((int)UpdaterExitCode.VerificationFailed, ReadResult(resultPath).ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsInstallFailureAndDoesNotRelaunch()
    {
        var root = CreateTempRoot();
        var msiPath = Path.Combine(root, "KoeNote.msi");
        await File.WriteAllTextAsync(msiPath, "installer");
        var options = new UpdaterOptions(
            msiPath,
            ComputeSha256("installer"),
            Path.Combine(root, "KoeNote.App.exe"),
            0,
            Path.Combine(root, "update.log"),
            Path.Combine(root, "update.result.json"),
            "0.20.0");
        var runner = new RecordingUpdaterProcessRunner { InstallExitCode = 1603 };
        var service = new UpdaterService(runner);

        var exitCode = await service.ExecuteAsync(options);

        Assert.Equal(UpdaterExitCode.InstallFailed, exitCode);
        Assert.Empty(runner.Starts);
        Assert.Contains("1603", ReadResult(options.ResultPath).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecuteAsync_ReportsRelaunchFailureWhenStartThrows()
    {
        var root = CreateTempRoot();
        var msiPath = Path.Combine(root, "KoeNote.msi");
        await File.WriteAllTextAsync(msiPath, "installer");
        var options = new UpdaterOptions(
            msiPath,
            ComputeSha256("installer"),
            Path.Combine(root, "missing", "KoeNote.App.exe"),
            0,
            Path.Combine(root, "update.log"),
            Path.Combine(root, "update.result.json"),
            "0.20.0");
        var runner = new RecordingUpdaterProcessRunner { ThrowOnStart = true };
        var service = new UpdaterService(runner);

        var exitCode = await service.ExecuteAsync(options);

        Assert.Equal(UpdaterExitCode.RelaunchFailed, exitCode);
        Assert.Equal((int)UpdaterExitCode.RelaunchFailed, ReadResult(options.ResultPath).ExitCode);
    }

    [Fact]
    public void Parse_RequiresValidArgumentsAndDefaultsResultPath()
    {
        var options = UpdaterOptions.Parse([
            "--msi", "KoeNote.msi",
            "--sha256", new string('a', 64),
            "--target-exe", "KoeNote.App.exe",
            "--parent-pid", "42",
            "--log", "update.log",
            "--version", "0.20.0"
        ]);

        Assert.Equal(42, options.ParentProcessId);
        Assert.EndsWith("update.result.json", options.ResultPath, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => UpdaterOptions.Parse(["--msi", "KoeNote.msi"]));
        Assert.Throws<ArgumentException>(() => UpdaterOptions.Parse([
            "--msi", "KoeNote.msi",
            "--sha256", "not-a-sha",
            "--target-exe", "KoeNote.App.exe",
            "--parent-pid", "42",
            "--log", "update.log",
            "--version", "0.20.0"
        ]));
    }

    private static UpdaterResult ReadResult(string path)
    {
        var result = JsonSerializer.Deserialize<UpdaterResult>(File.ReadAllText(path));
        return Assert.IsType<UpdaterResult>(result);
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private sealed class RecordingUpdaterProcessRunner : IUpdaterProcessRunner
    {
        public int InstallExitCode { get; init; }

        public bool ThrowOnStart { get; init; }

        public List<int> WaitedProcessIds { get; } = [];

        public List<RunCapture> Runs { get; } = [];

        public List<string> Starts { get; } = [];

        public Task WaitForExitAsync(int processId, CancellationToken cancellationToken)
        {
            WaitedProcessIds.Add(processId);
            return Task.CompletedTask;
        }

        public Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            Runs.Add(new RunCapture(fileName, [.. arguments]));
            return Task.FromResult(InstallExitCode);
        }

        public Task<bool> StartAsync(string fileName, CancellationToken cancellationToken)
        {
            if (ThrowOnStart)
            {
                throw new InvalidOperationException("Could not start.");
            }

            Starts.Add(fileName);
            return Task.FromResult(true);
        }
    }

    private sealed record RunCapture(string FileName, string[] Arguments);
}
