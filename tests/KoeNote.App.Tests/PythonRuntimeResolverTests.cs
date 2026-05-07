using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Tests;

public sealed class PythonRuntimeResolverTests
{
    [Theory]
    [InlineData(3, 9, true)]
    [InlineData(3, 11, true)]
    [InlineData(3, 12, true)]
    [InlineData(3, 8, false)]
    [InlineData(3, 13, false)]
    [InlineData(3, 14, false)]
    public void IsSupportedVersion_AllowsOnlyTorchCompatiblePythonRange(
        int major,
        int minor,
        bool expected)
    {
        Assert.Equal(expected, PythonRuntimeResolver.IsSupportedVersion(new Version(major, minor)));
    }

    [Fact]
    public void DiarizationRuntimeService_UsesDiarize012()
    {
        Assert.Equal("diarize==0.1.2", DiarizationRuntimeService.PackageSpec);
    }

    [Fact]
    public void FasterWhisperRuntimeService_UsesVerifiedPackageVersion()
    {
        Assert.Equal("faster-whisper==1.2.1", FasterWhisperRuntimeService.PackageSpec);
    }

    [Fact]
    public async Task DiarizationRuntimeService_CheckAsync_RejectsOlderDiarizeVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DiarizationPythonPath)!);
        File.WriteAllText(paths.DiarizationPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase)
        {
            [paths.DiarizationPythonPath] = new(
                0,
                TimeSpan.FromMilliseconds(1),
                "3.12.4|C:\\KoeNote\\python-envs\\diarization\\Scripts\\python.exe",
                string.Empty)
        })
        {
            VersionCheckResult = new ProcessRunResult(
                0,
                TimeSpan.FromMilliseconds(1),
                "0.1.1",
                string.Empty)
        };
        var service = new DiarizationRuntimeService(paths, runner);

        var result = await service.CheckAsync();

        Assert.False(result.IsAvailable);
        Assert.Contains("0.1.2 is required", result.Detail);
    }

    [Fact]
    public async Task DiarizationRuntimeService_CheckInstallPreflightAsync_UsesBundledPythonAndPip()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.BundledPythonPath)!);
        File.WriteAllText(paths.BundledPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase)
        {
            [paths.BundledPythonPath] = new(
                0,
                TimeSpan.FromMilliseconds(1),
                $"3.12.10|{paths.BundledPythonPath}",
                string.Empty)
        })
        {
            PipVersionResult = new ProcessRunResult(
                0,
                TimeSpan.FromMilliseconds(1),
                "pip 25.0.1",
                string.Empty)
        };
        var service = new DiarizationRuntimeService(paths, runner);

        var result = await service.CheckInstallPreflightAsync();

        Assert.True(result.IsReady);
        Assert.Contains("bundled Python", result.Message);
        Assert.Contains(runner.Arguments, arguments => arguments.SequenceEqual(["-m", "pip", "--version"]));
    }

    [Fact]
    public async Task DiarizationRuntimeService_InstallAsync_ClassifiesTorchWheelFailure()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DiarizationPythonPath)!);
        File.WriteAllText(paths.DiarizationPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase)
        {
            [paths.DiarizationPythonPath] = new(
                0,
                TimeSpan.FromMilliseconds(1),
                $"3.12.10|{paths.DiarizationPythonPath}",
                string.Empty)
        })
        {
            PipInstallResult = new ProcessRunResult(
                1,
                TimeSpan.FromMilliseconds(1),
                string.Empty,
                "ERROR: Could not find a version that satisfies the requirement torch<2.9,>=1.13")
        };
        var service = new DiarizationRuntimeService(paths, runner);

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Equal(DiarizationRuntimeService.FailureCategoryTorchWheelUnavailable, result.FailureCategory);
    }

    [Fact]
    public void BuildNoCompatiblePythonMessage_HidesPythonLauncherDiagnostics()
    {
        var message = PythonRuntimeResolver.BuildNoCompatiblePythonMessage(
        [
            "Python launcher 3.12: No suitable Python runtime found\r\nPass --list (-0) to see all detected environments on your machine",
            "Python launcher 3.11: No suitable Python runtime found\r\nor open the Microsoft Store to the requested version.",
            "python: Python 3.14.3 is unsupported."
        ]);

        Assert.Contains("Install Python 3.12 x64", message);
        Assert.Contains("Python 3.14.3 is unsupported.", message);
        Assert.DoesNotContain("Pass --list", message);
        Assert.DoesNotContain("Microsoft Store", message);
    }

    [Fact]
    public async Task ResolveInstallSourceAsync_PrefersBundledPythonRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBaseDirectory = Path.Combine(root, "app");
        var paths = new AppPaths(root, root, appBaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.BundledPythonPath)!);
        File.WriteAllText(paths.BundledPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase)
        {
            [paths.BundledPythonPath] = new(
                0,
                TimeSpan.FromMilliseconds(1),
                $"3.12.4|{paths.BundledPythonPath}",
                string.Empty)
        });
        var resolver = new PythonRuntimeResolver(paths, runner);

        var result = await resolver.ResolveInstallSourceAsync();

        Assert.True(result.IsFound);
        Assert.NotNull(result.Command);
        Assert.Equal(paths.BundledPythonPath, result.Command.FileName);
        Assert.Equal("bundled Python", result.Command.DisplayName);
        Assert.Equal([paths.BundledPythonPath], runner.FileNames);
    }

    [Fact]
    public void AppPaths_ProvidesManagedDiarizationPythonLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);

        Assert.EndsWith(Path.Combine("KoeNote", "python-envs", "diarization"), paths.DiarizationPythonEnvironment);
        Assert.EndsWith(Path.Combine("KoeNote", "python-envs", "diarization", "Scripts", "python.exe"), paths.DiarizationPythonPath);
    }

    [Fact]
    public void AppPaths_ProvidesManagedAsrPythonLayoutAndWhisperBaseModelPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);

        Assert.EndsWith(Path.Combine("KoeNote", "python-envs", "asr"), paths.AsrPythonEnvironment);
        Assert.EndsWith(Path.Combine("KoeNote", "python-envs", "asr", "Scripts", "python.exe"), paths.AsrPythonPath);
        Assert.EndsWith(Path.Combine("models", "asr", "whisper-base"), paths.WhisperBaseModelPath);
    }

    [Fact]
    public void FasterWhisperRuntimeLayout_DetectsManagedVenvPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper"));

        Assert.True(FasterWhisperRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public void FasterWhisperRuntimeLayout_RejectsManagedVenvDistInfoForOlderPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper"));
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper-1.1.1.dist-info"));

        Assert.False(FasterWhisperRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public async Task FasterWhisperRuntimeService_CheckInstallPreflightAsync_UsesBundledPythonAndPip()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.BundledPythonPath)!);
        File.WriteAllText(paths.BundledPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase)
        {
            [paths.BundledPythonPath] = new(
                0,
                TimeSpan.FromMilliseconds(1),
                $"3.12.10|{paths.BundledPythonPath}",
                string.Empty)
        })
        {
            PipVersionResult = new ProcessRunResult(
                0,
                TimeSpan.FromMilliseconds(1),
                "pip 25.0.1",
                string.Empty)
        };
        var service = new FasterWhisperRuntimeService(paths, runner);

        var result = await service.CheckInstallPreflightAsync();

        Assert.True(result.IsReady);
        Assert.Contains("bundled Python", result.Message);
        Assert.Contains(runner.Arguments, arguments => arguments.SequenceEqual(["-m", "pip", "--version"]));
    }

    [Fact]
    public async Task FasterWhisperRuntimeService_CheckAsync_RejectsUnexpectedVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AsrPythonPath)!);
        File.WriteAllText(paths.AsrPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase))
        {
            FasterWhisperVersionCheckResult = new ProcessRunResult(
                0,
                TimeSpan.FromMilliseconds(1),
                "1.1.1",
                string.Empty)
        };
        var service = new FasterWhisperRuntimeService(paths, runner);

        var result = await service.CheckAsync();

        Assert.False(result.IsAvailable);
        Assert.Contains("1.2.1 is required", result.Detail);
    }

    [Fact]
    public async Task FasterWhisperRuntimeService_InstallAsync_RecreatesUnsupportedManagedPython()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, Path.Combine(root, "app"));
        Directory.CreateDirectory(Path.GetDirectoryName(paths.AsrPythonPath)!);
        File.WriteAllText(paths.AsrPythonPath, string.Empty);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.BundledPythonPath)!);
        File.WriteAllText(paths.BundledPythonPath, string.Empty);
        var runner = new FakePythonProcessRunner(new Dictionary<string, ProcessRunResult>(StringComparer.OrdinalIgnoreCase)
        {
            [paths.BundledPythonPath] = new(0, TimeSpan.FromMilliseconds(1), $"3.12.10|{paths.BundledPythonPath}", string.Empty)
        })
        {
            PlainVersionResult = new ProcessRunResult(0, TimeSpan.FromMilliseconds(1), "3.14.0", string.Empty),
            PipInstallResult = new ProcessRunResult(1, TimeSpan.FromMilliseconds(1), string.Empty, "offline test stop")
        };
        var service = new FasterWhisperRuntimeService(paths, runner);

        var result = await service.InstallAsync();

        Assert.False(result.IsSucceeded);
        Assert.Contains(runner.Arguments, arguments => arguments.SequenceEqual(["-m", "venv", paths.AsrPythonEnvironment]));
    }

    [Fact]
    public void DiarizationRuntimeLayout_DetectsManagedVenvPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize"));

        Assert.True(DiarizationRuntimeLayout.HasManagedPackage(paths));
        Assert.True(DiarizationRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public void DiarizationRuntimeLayout_DetectsManagedVenvDistInfoForCurrentPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize-0.1.2.dist-info"));

        Assert.True(DiarizationRuntimeLayout.HasManagedPackage(paths));
        Assert.True(DiarizationRuntimeLayout.HasPackage(paths));
    }

    [Fact]
    public void DiarizationRuntimeLayout_RejectsManagedVenvDistInfoForOlderPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root, root, AppContext.BaseDirectory);
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize"));
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize-0.1.1.dist-info"));

        Assert.False(DiarizationRuntimeLayout.HasManagedPackage(paths));
        Assert.False(DiarizationRuntimeLayout.HasPackage(paths));
    }

    private sealed class FakePythonProcessRunner(
        IReadOnlyDictionary<string, ProcessRunResult> results) : ExternalProcessRunner
    {
        private readonly List<string> _fileNames = [];
        private readonly List<IReadOnlyList<string>> _arguments = [];

        public IReadOnlyList<string> FileNames => _fileNames;

        public IReadOnlyList<IReadOnlyList<string>> Arguments => _arguments;

        public ProcessRunResult? VersionCheckResult { get; init; }

        public ProcessRunResult? FasterWhisperVersionCheckResult { get; init; }

        public ProcessRunResult? PlainVersionResult { get; init; }

        public ProcessRunResult? PipVersionResult { get; init; }

        public ProcessRunResult? PipInstallResult { get; init; }

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            _fileNames.Add(fileName);
            _arguments.Add(arguments.ToArray());
            if (arguments.Contains("import importlib.metadata as md; print(md.version('diarize'))") &&
                VersionCheckResult is not null)
            {
                return Task.FromResult(VersionCheckResult);
            }

            if (arguments.Contains("import importlib.metadata as md; import faster_whisper; print(md.version('faster-whisper'))") &&
                FasterWhisperVersionCheckResult is not null)
            {
                return Task.FromResult(FasterWhisperVersionCheckResult);
            }

            if (arguments.Contains("import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}')") &&
                PlainVersionResult is not null)
            {
                return Task.FromResult(PlainVersionResult);
            }

            if (arguments.SequenceEqual(["-m", "pip", "--version"]) &&
                PipVersionResult is not null)
            {
                return Task.FromResult(PipVersionResult);
            }

            if (arguments.Contains("-m") &&
                arguments.Contains("pip") &&
                arguments.Contains("install") &&
                PipInstallResult is not null)
            {
                return Task.FromResult(PipInstallResult);
            }

            return Task.FromResult(results.TryGetValue(fileName, out var result)
                ? result
                : new ProcessRunResult(
                    1,
                    TimeSpan.FromMilliseconds(1),
                    string.Empty,
                    $"{fileName} not found"));
        }
    }
}
