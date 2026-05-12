using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlamaRuntimePathBridgeTests
{
    private const string UnicodeUserName = "\u5869\u6fa4\u9ebb\u5b50";
    private const string UnicodeModelFileName = "\u30e2\u30c7\u30eb.gguf";
    private const string UnicodeOutputDirectoryName = "\u65e5\u672c\u8a9e";

    [Fact]
    public void Create_MapsUnicodeModelPathToAsciiSafePath()
    {
        var unicodeRoot = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", UnicodeUserName, Guid.NewGuid().ToString("N"));
        var modelPath = Path.Combine(unicodeRoot, "models", "review", UnicodeModelFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "model");

        string bridgeDirectory;
        using (var bridge = LlamaRuntimePathBridge.Create(modelPath))
        {
            bridgeDirectory = Path.GetDirectoryName(bridge.ModelPath)!;
            Assert.True(File.Exists(bridge.ModelPath));
            Assert.True(IsAscii(bridge.ModelPath));
            Assert.Equal("model.gguf", Path.GetFileName(bridge.ModelPath));
        }

        Assert.False(Directory.Exists(bridgeDirectory));
    }

    [Fact]
    public async Task ReviewWorker_UsesAsciiSafeRuntimePathsForUnicodeModelPromptAndSchemaPaths()
    {
        var paths = CreateUnicodeReadyPaths();
        var runtimePath = PrepareRuntime(paths);
        var modelPath = PrepareUnicodeModel(paths);
        var runner = new CapturingReviewProcessRunner();
        var worker = new ReviewWorker(
            runner,
            new ReviewCommandBuilder(),
            new ReviewPromptBuilder(),
            new ReviewJsonNormalizer(),
            new ReviewResultStore(),
            new CorrectionDraftRepository(paths));

        await worker.RunAsync(new ReviewRunOptions(
            "job-001",
            runtimePath,
            modelPath,
            Path.Combine(paths.Root, "jobs", "job-001", UnicodeOutputDirectoryName),
            [
                new TranscriptSegment("000001", "job-001", 0, 1, "Speaker_0", "hello", "hello")
            ]));

        var modelArgument = runner.ArgumentAfter("--model");
        var promptArgument = runner.ArgumentAfter("--file");
        var schemaArgument = runner.ArgumentAfter("--json-schema-file");
        Assert.True(IsAscii(modelArgument), modelArgument);
        Assert.True(IsAscii(promptArgument), promptArgument);
        Assert.True(IsAscii(schemaArgument), schemaArgument);
        Assert.True(runner.ModelFileExistedAtRun);
        Assert.True(runner.PromptFileExistedAtRun);
        Assert.True(runner.SchemaFileExistedAtRun);
    }

    [Fact]
    public async Task TranscriptPolishingRuntime_UsesAsciiSafeRuntimePathsForUnicodeModelAndPromptPaths()
    {
        var paths = CreateUnicodeReadyPaths();
        var runtimePath = PrepareRuntime(paths);
        var modelPath = PrepareUnicodeModel(paths);
        var runner = new CapturingLlamaProcessRunner("polished");
        var runtime = new LlamaTranscriptPolishingRuntime(runner, new TranscriptPolishingPromptBuilder());

        await runtime.PolishChunkAsync(
            new TranscriptPolishingOptions("job-001", runtimePath, modelPath, Path.Combine(paths.Root, "polish", UnicodeOutputDirectoryName), "model"),
            new TranscriptPolishingChunk(1, [
                new TranscriptReadModel("000001", 0, 1, "Speaker_0", "hello", "", "Speaker_0", "hello", null, null)
            ]));

        Assert.True(IsAscii(runner.ArgumentAfter("--model")));
        Assert.True(IsAscii(runner.ArgumentAfter("--file")));
        Assert.True(runner.ModelFileExistedAtRun);
        Assert.True(runner.PromptFileExistedAtRun);
    }

    [Fact]
    public async Task TranscriptSummaryRuntime_UsesAsciiSafeRuntimePathsForUnicodeModelAndPromptPaths()
    {
        var paths = CreateUnicodeReadyPaths();
        var runtimePath = PrepareRuntime(paths);
        var modelPath = PrepareUnicodeModel(paths);
        var runner = new CapturingLlamaProcessRunner("## Overview\n\nSummary.");
        var runtime = new LlamaTranscriptSummaryRuntime(runner, new TranscriptSummaryPromptBuilder());

        await runtime.SummarizeChunkAsync(
            new TranscriptSummaryOptions("job-001", runtimePath, modelPath, Path.Combine(paths.Root, "summary", UnicodeOutputDirectoryName), "model"),
            new TranscriptSummaryChunk(1, "transcript", "000001", 0, 1, "hello"));

        Assert.True(IsAscii(runner.ArgumentAfter("--model")));
        Assert.True(IsAscii(runner.ArgumentAfter("--file")));
        Assert.True(runner.ModelFileExistedAtRun);
        Assert.True(runner.PromptFileExistedAtRun);
    }

    private static AppPaths CreateUnicodeReadyPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", UnicodeUserName, Guid.NewGuid().ToString("N"), "roaming");
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", UnicodeUserName, Guid.NewGuid().ToString("N"), "local");
        var paths = new AppPaths(root, local);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        return paths;
    }

    private static string PrepareRuntime(AppPaths paths)
    {
        var runtimePath = Path.Combine(paths.Root, "runtime", "llama-completion.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimePath)!);
        File.WriteAllText(runtimePath, "runtime");
        return runtimePath;
    }

    private static string PrepareUnicodeModel(AppPaths paths)
    {
        var modelPath = Path.Combine(paths.UserModels, "review", UnicodeModelFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "model");
        return modelPath;
    }

    private static bool IsAscii(string value)
    {
        return value.All(static character => character <= 0x7f);
    }

    private sealed class CapturingReviewProcessRunner : ExternalProcessRunner
    {
        private IReadOnlyList<string> _arguments = [];
        public bool ModelFileExistedAtRun { get; private set; }
        public bool PromptFileExistedAtRun { get; private set; }
        public bool SchemaFileExistedAtRun { get; private set; }

        public string ArgumentAfter(string name)
        {
            var index = _arguments
                .Select((argument, argumentIndex) => (argument, argumentIndex))
                .First(item => item.argument == name)
                .argumentIndex;
            return _arguments[index + 1];
        }

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            _arguments = arguments;
            ModelFileExistedAtRun = File.Exists(ArgumentAfter("--model"));
            PromptFileExistedAtRun = File.Exists(ArgumentAfter("--file"));
            SchemaFileExistedAtRun = File.Exists(ArgumentAfter("--json-schema-file"));
            return Task.FromResult(new ProcessRunResult(0, TimeSpan.FromMilliseconds(10), "[]", ""));
        }
    }

    private sealed class CapturingLlamaProcessRunner(string standardOutput) : ExternalProcessRunner
    {
        private IReadOnlyList<string> _arguments = [];
        public bool ModelFileExistedAtRun { get; private set; }
        public bool PromptFileExistedAtRun { get; private set; }

        public string ArgumentAfter(string name)
        {
            var index = _arguments
                .Select((argument, argumentIndex) => (argument, argumentIndex))
                .First(item => item.argument == name)
                .argumentIndex;
            return _arguments[index + 1];
        }

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            _arguments = arguments;
            ModelFileExistedAtRun = File.Exists(ArgumentAfter("--model"));
            PromptFileExistedAtRun = File.Exists(ArgumentAfter("--file"));
            return Task.FromResult(new ProcessRunResult(0, TimeSpan.FromMilliseconds(10), standardOutput, ""));
        }
    }
}
