using System.Text.Json;
using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;

namespace KoeNote.App.Tests;

public sealed class ScriptedDiarizationServiceTests
{
    [Fact]
    public async Task RunAsync_FailsBeforeWorkerWhenRequiredRuntimeDataIsMissing()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;
        File.WriteAllText(paths.DiarizeWorkerScriptPath, "# worker");
        CreateManagedPythonRuntime(paths);
        CreateManagedPackageMetadata(paths);
        var runner = new CapturingProcessRunner();
        var service = new ScriptedDiarizationService(
            paths,
            runner,
            new DiarizationJsonNormalizer(),
            new DiarizationSegmentAssigner(),
            new TranscriptSegmentRepository(paths),
            new AsrResultStore());
        var segments = new[]
        {
            new TranscriptSegment("000001", "job-001", 0, 1, null, "raw", "raw")
        };

        var result = await service.RunAsync(
            "job-001",
            Path.Combine(paths.Root, "meeting.wav"),
            segments,
            CancellationToken.None);

        Assert.DoesNotContain(runner.Arguments, arguments => arguments.Contains(paths.DiarizeWorkerScriptPath));
        Assert.Contains("silero_vad.jit", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Runtime package data is missing", result.Status, StringComparison.OrdinalIgnoreCase);
        var statusJson = JsonDocument.Parse(File.ReadAllText(result.RawOutputPath));
        Assert.Contains("silero_vad.jit", statusJson.RootElement.GetProperty("status").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_ValidatesSelectedManagedRuntimeEvenWhenLegacyPackageIsValid()
    {
        var paths = TestDatabase.CreateRepositoryFixture().Paths;
        File.WriteAllText(paths.DiarizeWorkerScriptPath, "# worker");
        CreateManagedPythonRuntime(paths);
        CreateManagedPackageMetadata(paths);
        CreateLegacyPackage(paths);
        var runner = new CapturingProcessRunner();
        var service = new ScriptedDiarizationService(
            paths,
            runner,
            new DiarizationJsonNormalizer(),
            new DiarizationSegmentAssigner(),
            new TranscriptSegmentRepository(paths),
            new AsrResultStore());
        var segments = new[]
        {
            new TranscriptSegment("000001", "job-001", 0, 1, null, "raw", "raw")
        };

        var result = await service.RunAsync(
            "job-001",
            Path.Combine(paths.Root, "meeting.wav"),
            segments,
            CancellationToken.None);

        Assert.DoesNotContain(runner.Arguments, arguments => arguments.Contains(paths.DiarizeWorkerScriptPath));
        Assert.Contains("silero_vad.jit", result.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(paths.DiarizationPythonEnvironment, result.Status, StringComparison.OrdinalIgnoreCase);
    }

    private static void CreateManagedPythonRuntime(AppPaths paths)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DiarizationPythonPath)!);
        File.WriteAllText(paths.DiarizationPythonPath, string.Empty);
    }

    private static void CreateManagedPackageMetadata(AppPaths paths)
    {
        var sitePackages = Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages");
        Directory.CreateDirectory(Path.Combine(sitePackages, "diarize"));
        Directory.CreateDirectory(Path.Combine(sitePackages, $"{DiarizationRuntimeService.PackageName}-{DiarizationRuntimeService.RequiredPackageVersion}.dist-info"));
    }

    private static void CreateLegacyPackage(AppPaths paths)
    {
        Directory.CreateDirectory(Path.Combine(paths.PythonPackages, "diarize"));
        var sileroData = Path.Combine(paths.PythonPackages, "silero_vad", "data");
        Directory.CreateDirectory(sileroData);
        File.WriteAllText(Path.Combine(sileroData, "silero_vad.jit"), string.Empty);
    }

    private sealed class CapturingProcessRunner : ExternalProcessRunner
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            Arguments.Add(arguments);
            return Task.FromResult(new ProcessRunResult(
                0,
                TimeSpan.Zero,
                $"3.12.10|{fileName}",
                string.Empty));
        }
    }
}
