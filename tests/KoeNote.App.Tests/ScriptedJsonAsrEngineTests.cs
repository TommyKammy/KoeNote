using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Jobs;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class ScriptedJsonAsrEngineTests
{
    [Fact]
    public void FasterWhisperWorker_DoesNotSilentlyFallbackToCpu()
    {
        var scriptPath = FindRepositoryFasterWhisperScriptPath();
        var script = File.ReadAllText(scriptPath);

        Assert.DoesNotContain("retrying on CPU", script, StringComparison.Ordinal);
        Assert.DoesNotContain("device=\"cpu\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("compute_type=\"int8\"", script, StringComparison.Ordinal);
    }

    [Fact]
    public void FasterWhisperWorker_EmitsRuntimeDiagnostics()
    {
        var scriptPath = FindRepositoryFasterWhisperScriptPath();
        var script = File.ReadAllText(scriptPath);

        Assert.Contains("koenote_asr_diagnostic", script);
        Assert.Contains("faster_whisper_version", script);
        Assert.Contains("ctranslate2_version", script);
        Assert.Contains("ctranslate2_cuda_device_count", script);
        Assert.Contains("selected_cuda_device_index", script);
        Assert.Contains("cuda_visible_devices", script);
        Assert.Contains("supported_compute_types_cuda", script);
        Assert.Contains("NVIDIA_SMI_GPU_QUERY", script);
        Assert.Contains("index,name,driver_version,memory.total,memory.free,compute_cap", script);
        Assert.Contains("parse_nvidia_smi_gpus", script);
        Assert.Contains("memory_total_mb", script);
        Assert.Contains("memory_free_mb", script);
        Assert.Contains("KOENOTE_ASR_TOOLS_DIR", script);
        Assert.Contains("KOENOTE_CTRANSLATE2_CUDA_DIR", script);
        Assert.Contains("ctranslate2*.dll", script);
        Assert.Contains("_ext*.pyd", script);
        Assert.Contains("nvidia-smi", script);
        Assert.Contains("gpu_probe", script);
        Assert.Contains("gpu_memory_snapshot", script);
        Assert.Contains("before_model_load", script);
        Assert.Contains("after_model_load", script);
        Assert.Contains("before_transcribe", script);
        Assert.Contains("model_load_failed", script);
        Assert.Contains("transcribe_failed", script);
        Assert.Contains("transcribe_start", script);
        Assert.Contains("--execution-profile", script);
        Assert.Contains("--chunk-seconds", script);
        Assert.DoesNotContain("\"tools\", \"asr\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranscribeAsync_UsesDedicatedCTranslate2CudaPathWithoutAsrToolsPath()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "asr", "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tools", "asr"));
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "tools", "asr-ctranslate2-cuda"));
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
        var pathEntries = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var asrToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        var ctranslate2CudaDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "asr-ctranslate2-cuda");
        Assert.DoesNotContain(asrToolsDirectory, pathEntries, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            ctranslate2CudaDirectory,
            pathEntries,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal(
            asrToolsDirectory,
            runner.Environment["KOENOTE_ASR_TOOLS_DIR"]);
        Assert.Equal(
            ctranslate2CudaDirectory,
            runner.Environment["KOENOTE_CTRANSLATE2_CUDA_DIR"]);
        Assert.StartsWith(
            ctranslate2CudaDirectory,
            path,
            StringComparison.OrdinalIgnoreCase);

        var logPath = Assert.Single(Directory.GetFiles(Path.Combine(paths.Jobs, "job-001", "logs"), "asr-*.log"));
        var log = File.ReadAllText(logPath);
        Assert.Contains("engine_id: scripted-test", log);
        Assert.Contains("display_name: Scripted test", log);
        Assert.Contains("runtime_path: python", log);
        Assert.Contains($"worker_script_path: {scriptPath}", log);
        Assert.Contains($"model_path: {modelPath}", log);
        Assert.Contains($"normalized_audio_path: {audioPath}", log);
        Assert.Contains("output_json_path:", log);
        Assert.Contains("exit_code: 0", log);
        Assert.Contains("exit_summary: success", log);
        Assert.Contains("koenote_asr_tools_dir:", log);
        Assert.Contains("koenote_ctranslate2_cuda_dir:", log);
        Assert.Contains("## stdout", log);
        Assert.Contains("ok", log);
    }

    [Fact]
    public async Task TranscribeAsync_PassesExplicitFasterWhisperGpuProfileArguments()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('ok')");
        File.WriteAllText(audioPath, "");
        Directory.CreateDirectory(modelPath);
        var runner = new CapturingAsrProcessRunner();
        var engine = CreateEngine(paths, runner);

        await engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                modelPath,
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "faster-whisper-large-v3-turbo",
                scriptPath),
            new AsrOptions(
                Device: "cuda",
                ComputeType: "float16",
                ExecutionProfileId: AsrExecutionProfiles.CudaFloat16,
                AttemptNumber: 2,
                ChunkSeconds: 300));

        Assert.NotNull(runner.Arguments);
        Assert.Contains("--device", runner.Arguments);
        Assert.Contains("cuda", runner.Arguments);
        Assert.Contains("--compute-type", runner.Arguments);
        Assert.Contains("float16", runner.Arguments);
        Assert.Contains("--execution-profile", runner.Arguments);
        Assert.Contains(AsrExecutionProfiles.CudaFloat16, runner.Arguments);
        Assert.Contains("--attempt-number", runner.Arguments);
        Assert.Contains("2", runner.Arguments);
        Assert.Contains("--chunk-seconds", runner.Arguments);
        Assert.Contains("300", runner.Arguments);

        var logPath = Assert.Single(Directory.GetFiles(Path.Combine(paths.Jobs, "job-001", "logs"), "asr-*.log"));
        var log = File.ReadAllText(logPath);
        Assert.Contains("requested_device: cuda", log);
        Assert.Contains("requested_compute_type: float16", log);
        Assert.Contains($"execution_profile_id: {AsrExecutionProfiles.CudaFloat16}", log);
        Assert.Contains("attempt_number: 2", log);
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

    [Fact]
    public async Task TranscribeAsync_ClassifiesCudaRuntimeFailures()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('should fail')");
        File.WriteAllText(audioPath, "");
        Directory.CreateDirectory(modelPath);
        var engine = CreateEngine(paths, new FailingAsrProcessRunner("Could not load library cublas64_12.dll"));

        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() => engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                modelPath,
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "model",
                scriptPath),
            new AsrOptions()));

        Assert.Equal(AsrFailureCategory.CudaRuntimeMissing, exception.Category);
        Assert.NotNull(exception.WorkerLogPath);
        Assert.True(File.Exists(exception.WorkerLogPath));
        Assert.Contains("Worker log:", exception.Message);
        Assert.Equal("failed", ReadSingleRunStatus(paths));
    }

    [Fact]
    public async Task TranscribeAsync_DescribesNativeCrashExitCodeInFailureAndWorkerLog()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('should fail')");
        File.WriteAllText(audioPath, "");
        Directory.CreateDirectory(modelPath);
        var engine = CreateEngine(paths, new ExitCodeAsrProcessRunner(-1073740791));

        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() => engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                modelPath,
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "model",
                scriptPath),
            new AsrOptions()));

        Assert.Equal(AsrFailureCategory.NativeCrash, exception.Category);
        Assert.Contains("0xC0000409 STATUS_STACK_BUFFER_OVERRUN/native fail-fast", exception.Message);
        Assert.NotNull(exception.WorkerLogPath);
        var log = File.ReadAllText(exception.WorkerLogPath);
        Assert.Contains("exit_code: -1073740791", log);
        Assert.Contains("exit_summary: 0xC0000409 STATUS_STACK_BUFFER_OVERRUN/native fail-fast", log);
        Assert.Equal("NativeCrash", ReadSingleRunErrorCategory(paths));
    }

    [Theory]
    [InlineData("CUDA out of memory while running CTranslate2")]
    [InlineData("CUDA driver version is insufficient for CUDA runtime version")]
    [InlineData("CTranslate2 failed before decoding")]
    public async Task TranscribeAsync_DoesNotClassifyGenericCudaFailuresAsMissingRuntime(string standardError)
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('should fail')");
        File.WriteAllText(audioPath, "");
        Directory.CreateDirectory(modelPath);
        var engine = CreateEngine(paths, new FailingAsrProcessRunner(standardError));

        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() => engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                modelPath,
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "model",
                scriptPath),
            new AsrOptions()));

        Assert.Equal(AsrFailureCategory.ProcessFailed, exception.Category);
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
            new AsrRunRepository(paths),
            new JobLogRepository(paths));
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

    private static string ReadSingleRunErrorCategory(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT error_category FROM asr_runs;";
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }

    private static string FindRepositoryFasterWhisperScriptPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "scripts", "asr", "faster_whisper_transcribe.py");
            if (File.Exists(candidate) && new FileInfo(candidate).Length > 0)
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Could not find scripts/asr/faster_whisper_transcribe.py.");
    }

    private sealed class CapturingAsrProcessRunner : ExternalProcessRunner
    {
        public IReadOnlyDictionary<string, string>? Environment { get; private set; }
        public IReadOnlyList<string>? Arguments { get; private set; }

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            Environment = environment;
            Arguments = arguments;
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

    private sealed class FailingAsrProcessRunner(string standardError) : ExternalProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            return Task.FromResult(new ProcessRunResult(1, TimeSpan.FromSeconds(1), string.Empty, standardError));
        }
    }

    private sealed class ExitCodeAsrProcessRunner(int exitCode) : ExternalProcessRunner
    {
        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            return Task.FromResult(new ProcessRunResult(exitCode, TimeSpan.FromSeconds(1), string.Empty, string.Empty));
        }
    }
}
