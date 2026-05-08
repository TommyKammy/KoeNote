using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class ScriptedJsonAsrEngineTests
{
    [Fact]
    public async Task TranscribeAsync_AddsBundledAsrToolsToProcessPath()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "asr", "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tools", "asr"));
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(scriptPath, "print('ok')");
        File.WriteAllText(audioPath, "");
        var runner = new CapturingAsrProcessRunner();
        var engine = CreateEngine(paths, runner);

        await engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                modelPath,
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "model",
                scriptPath),
            new AsrOptions());

        Assert.NotNull(runner.Environment);
        Assert.True(runner.Environment.TryGetValue("PATH", out var path));
        Assert.Contains(
            Path.Combine(AppContext.BaseDirectory, "tools", "asr"),
            path,
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            Path.Combine(AppContext.BaseDirectory, "tools", "asr"),
            runner.Environment["KOENOTE_ASR_TOOLS_DIR"]);
    }

    [Fact]
    public async Task TranscribeAsync_RejectsMissingModelBeforeStartingProcess()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        File.WriteAllText(scriptPath, "print('should not run')");
        File.WriteAllText(audioPath, "");
        var engine = CreateEngine(paths);

        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() => engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                Path.Combine(paths.Root, "missing-model"),
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "missing-model",
                scriptPath),
            new AsrOptions()));

        Assert.Equal(AsrFailureCategory.MissingModel, exception.Category);
        Assert.Equal("failed", ReadSingleRunStatus(paths));
    }

    private static ScriptedJsonAsrEngine CreateEngine(AppPaths paths, ExternalProcessRunner? processRunner = null)
    {
        return new ScriptedJsonAsrEngine(
            "scripted-test",
            "Scripted test",
            "scripted",
            processRunner ?? new ExternalProcessRunner(),
            new AsrJsonNormalizer(),
            new AsrResultStore(),
            new TranscriptSegmentRepository(paths),
            new AsrRunRepository(paths));
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
    }

    private static string ReadSingleRunStatus(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM asr_runs;";
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private sealed class CapturingAsrProcessRunner : ExternalProcessRunner
    {
        public IReadOnlyDictionary<string, string>? Environment { get; private set; }

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            Environment = environment;
            var outputJsonIndex = arguments
                .Select((argument, index) => (argument, index))
                .First(item => item.argument == "--output-json")
                .index;
            var outputJsonPath = arguments[outputJsonIndex + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath)!);
            File.WriteAllText(outputJsonPath, """
                {
                  "segments": [
                    { "start": 0.0, "end": 1.0, "text": "hello" }
                  ]
                }
                """);
            return Task.FromResult(new ProcessRunResult(0, TimeSpan.FromSeconds(1), "ok", string.Empty));
        }
    }
}
