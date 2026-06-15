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
    public void FasterWhisperWorker_WritesOutputBeforeBypassingCudaTeardown()
    {
        var scriptPath = FindRepositoryFasterWhisperScriptPath();
        var script = File.ReadAllText(scriptPath);
        var mainIndex = script.IndexOf("def main() -> int:", StringComparison.Ordinal);
        var writeIndex = script.LastIndexOf("write_result_payload(", StringComparison.Ordinal);
        var bypassIndex = script.LastIndexOf("if bypass_cuda_teardown:", StringComparison.Ordinal);

        Assert.True(mainIndex >= 0);
        Assert.True(writeIndex > mainIndex);
        Assert.True(bypassIndex > writeIndex);
        Assert.Contains("os._exit(0)", script);
    }

    [Fact]
    public void FasterWhisperWorker_BypassesAutoCudaTeardownWhenCudaIsAvailable()
    {
        var scriptPath = FindRepositoryFasterWhisperScriptPath();
        var script = File.ReadAllText(scriptPath);
        var predicateIndex = script.IndexOf("def should_bypass_cuda_teardown_after_success", StringComparison.Ordinal);

        Assert.True(predicateIndex >= 0);
        Assert.Contains("requested_device == \"auto\" and is_ctranslate2_cuda_available()", script);
        Assert.Contains("\"cuda\" in model_device", script);
    }

    [Fact]
    public void FasterWhisperWorker_ReleasesCudaModelBeforePyannoteDiarizationAfterPersistingAsrOutput()
    {
        var scriptPath = FindRepositoryFasterWhisperScriptPath();
        var script = File.ReadAllText(scriptPath);
        var mainIndex = script.IndexOf("def main() -> int:", StringComparison.Ordinal);
        var modelReleaseGuardIndex = script.IndexOf("if model is not None:", mainIndex, StringComparison.Ordinal);
        var cudaPrewriteGuardIndex = script.IndexOf("if bypass_cuda_teardown:", modelReleaseGuardIndex, StringComparison.Ordinal);
        var pendingWriteIndex = script.IndexOf("\"skipped: diarization_not_completed\"", mainIndex, StringComparison.Ordinal);
        var releaseIndex = script.IndexOf("del model_to_release", mainIndex, StringComparison.Ordinal);
        var diarizationIndex = script.IndexOf("run_pyannote_diarization(", mainIndex, StringComparison.Ordinal);

        Assert.True(mainIndex >= 0);
        Assert.True(modelReleaseGuardIndex > mainIndex);
        Assert.True(cudaPrewriteGuardIndex > modelReleaseGuardIndex);
        Assert.True(pendingWriteIndex > cudaPrewriteGuardIndex);
        Assert.True(releaseIndex > pendingWriteIndex);
        Assert.True(diarizationIndex > releaseIndex);
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
        Assert.Contains("index,name,driver_version,memory.total,memory.free", script);
        Assert.Contains("NVIDIA_SMI_COMPUTE_CAP_QUERY", script);
        Assert.Contains("index,compute_cap", script);
        Assert.Contains("parse_nvidia_smi_gpus", script);
        Assert.Contains("parse_nvidia_smi_compute_caps", script);
        Assert.Contains("compute_cap_error", script);
        Assert.Contains("memory_total_mb", script);
        Assert.Contains("memory_free_mb", script);
        Assert.Contains("KOENOTE_ASR_TOOLS_DIR", script);
        Assert.Contains("KOENOTE_CTRANSLATE2_CUDA_DIR", script);
        Assert.Contains("has_nvidia_runtime_files", script);
        Assert.Contains("ctranslate2*.dll", script);
        Assert.Contains("_ext*.pyd", script);
        Assert.Contains("nvidia-smi", script);
        Assert.Contains("gpu_probe", script);
        Assert.Contains("gpu_memory_snapshot", script);
        Assert.Contains("should_probe_gpu_memory", script);
        Assert.Contains("before_model_load", script);
        Assert.Contains("after_model_load", script);
        Assert.Contains("before_transcribe", script);
        Assert.Contains("model_load_failed", script);
        Assert.Contains("transcribe_failed", script);
        Assert.Contains("transcribe_start", script);
        Assert.Contains("--execution-profile", script);
        Assert.Contains("--chunk-seconds", script);
        Assert.Contains("\"tools\", \"asr\")", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TranscribeAsync_UsesDedicatedCTranslate2CudaPathWithoutAsrToolsPathWhenAsrToolsHasNoNvidiaDlls()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "asr", "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        var asrToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        Directory.CreateDirectory(asrToolsDirectory);
        DeleteLegacyNvidiaDlls(asrToolsDirectory);
        var ctranslate2CudaDirectory = paths.AsrCTranslate2RuntimeDirectory;
        Directory.CreateDirectory(ctranslate2CudaDirectory);
        DeleteLegacyNvidiaDlls(ctranslate2CudaDirectory);
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
    public async Task TranscribeAsync_AddsLegacyAsrToolsPathWhenItContainsNvidiaDlls()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "asr", "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        var asrToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        var ctranslate2CudaDirectory = paths.AsrCTranslate2RuntimeDirectory;
        Directory.CreateDirectory(asrToolsDirectory);
        DeleteLegacyNvidiaDlls(asrToolsDirectory);
        Directory.CreateDirectory(ctranslate2CudaDirectory);
        DeleteLegacyNvidiaDlls(ctranslate2CudaDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(scriptPath, "print('ok')");
        File.WriteAllText(audioPath, "");
        File.WriteAllText(Path.Combine(asrToolsDirectory, "cublas64_12.dll"), "");
        File.WriteAllText(Path.Combine(asrToolsDirectory, "cublasLt64_12.dll"), "");
        File.WriteAllText(Path.Combine(asrToolsDirectory, "cudart64_12.dll"), "");
        File.WriteAllText(Path.Combine(asrToolsDirectory, "cudnn64_9.dll"), "");
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
        Assert.Contains(
            ctranslate2CudaDirectory,
            pathEntries,
            StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            asrToolsDirectory,
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

        DeleteLegacyNvidiaDlls(asrToolsDirectory);
    }

    [Fact]
    public async Task TranscribeAsync_DoesNotAddLegacyAsrToolsPathWhenDedicatedCudaRuntimeIsComplete()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(AppContext.BaseDirectory, "scripts", "asr", "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        var asrToolsDirectory = Path.Combine(AppContext.BaseDirectory, "tools", "asr");
        var ctranslate2CudaDirectory = paths.AsrCTranslate2RuntimeDirectory;
        Directory.CreateDirectory(asrToolsDirectory);
        DeleteLegacyNvidiaDlls(asrToolsDirectory);
        Directory.CreateDirectory(ctranslate2CudaDirectory);
        DeleteLegacyNvidiaDlls(ctranslate2CudaDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(scriptPath)!);
        Directory.CreateDirectory(modelPath);
        File.WriteAllText(scriptPath, "print('ok')");
        File.WriteAllText(audioPath, "");
        WriteNvidiaDllSet(asrToolsDirectory);
        WriteNvidiaDllSet(ctranslate2CudaDirectory);
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
        Assert.Contains(
            ctranslate2CudaDirectory,
            pathEntries,
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            asrToolsDirectory,
            pathEntries,
            StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith(
            ctranslate2CudaDirectory,
            path,
            StringComparison.OrdinalIgnoreCase);

        DeleteLegacyNvidiaDlls(asrToolsDirectory);
        DeleteLegacyNvidiaDlls(ctranslate2CudaDirectory);
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
    public async Task TranscribeAsync_ChunkedGpuAsr_SplitsWavAndOffsetsMergedSegments()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('ok')");
        WriteSilentPcmWav(audioPath, sampleRate: 10, durationSeconds: 2.4);
        Directory.CreateDirectory(modelPath);
        var runner = new ChunkedAsrProcessRunner();
        var engine = CreateEngine(paths, runner);

        var result = await engine.TranscribeAsync(
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
                AttemptNumber: 1,
                ChunkSeconds: 1));

        Assert.Equal(3, runner.Arguments.Count);
        Assert.All(runner.Arguments, arguments =>
        {
            Assert.Contains("--device", arguments);
            Assert.Contains("cuda", arguments);
            Assert.DoesNotContain("--chunk-seconds", arguments);
        });
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal("000001", result.Segments[0].SegmentId);
        Assert.Equal("000002", result.Segments[1].SegmentId);
        Assert.Equal("000003", result.Segments[2].SegmentId);
        Assert.Equal(0.1, result.Segments[0].StartSeconds, precision: 3);
        Assert.Equal(1.1, result.Segments[1].StartSeconds, precision: 3);
        Assert.Equal(2.1, result.Segments[2].StartSeconds, precision: 3);
        Assert.Equal("chunk 1", result.Segments[0].RawText);
        Assert.Equal("chunk 2", result.Segments[1].RawText);
        Assert.Equal("chunk 3", result.Segments[2].RawText);
        Assert.Equal(TimeSpan.FromSeconds(3), result.Duration);

        var rawOutput = File.ReadAllText(result.RawOutputPath);
        Assert.Contains("\"segments\"", rawOutput);
        Assert.Contains("\"chunk 3\"", rawOutput);
        var logDirectory = Path.Combine(paths.Jobs, "job-001", "logs");
        Assert.Equal(3, Directory.GetFiles(logDirectory, "asr-*-chunk-*.log").Length);
        var firstChunkLog = File.ReadAllText(Directory.GetFiles(logDirectory, "asr-*-chunk-001.log").Single());
        Assert.Contains("chunk_index: 1", firstChunkLog);
        Assert.Contains("chunk_count: 3", firstChunkLog);
        Assert.Contains("chunk_offset_seconds: 0.000", firstChunkLog);
        Assert.Equal("succeeded", ReadSingleRunStatus(paths));
    }

    [Fact]
    public async Task TranscribeAsync_ChunkedGpuAsr_ReportsChunkFailureWithWorkerLog()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('ok')");
        WriteSilentPcmWav(audioPath, sampleRate: 10, durationSeconds: 2.4);
        Directory.CreateDirectory(modelPath);
        var runner = new ChunkedAsrProcessRunner(failAtChunk: 2);
        var engine = CreateEngine(paths, runner);

        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() => engine.TranscribeAsync(
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
                ChunkSeconds: 1)));

        Assert.Equal(AsrFailureCategory.ProcessFailed, exception.Category);
        Assert.Contains("chunk 2/3", exception.Message);
        Assert.Contains("Worker log:", exception.Message);
        Assert.NotNull(exception.WorkerLogPath);
        Assert.True(File.Exists(exception.WorkerLogPath));
        var log = File.ReadAllText(exception.WorkerLogPath);
        Assert.Contains("chunk_index: 2", log);
        Assert.Contains("chunk_count: 3", log);
        Assert.Equal("failed", ReadSingleRunStatus(paths));
        Assert.Equal("ProcessFailed", ReadSingleRunErrorCategory(paths));
    }

    [Fact]
    public async Task TranscribeAsync_ChunkedGpuAsr_RejectsStaleChunkJsonAfterWorkerFailure()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        var outputDirectory = Path.Combine(paths.Jobs, "job-001", "asr");
        File.WriteAllText(scriptPath, "print('ok')");
        WriteSilentPcmWav(audioPath, sampleRate: 10, durationSeconds: 2.4);
        Directory.CreateDirectory(modelPath);
        var config = new AsrEngineConfig(
            "python",
            modelPath,
            outputDirectory,
            "faster-whisper-large-v3-turbo",
            scriptPath);
        var options = new AsrOptions(
            Device: "cuda",
            ComputeType: "float16",
            ExecutionProfileId: AsrExecutionProfiles.CudaFloat16,
            ChunkSeconds: 1);
        var firstEngine = CreateEngine(paths, new ChunkedAsrProcessRunner());

        var firstResult = await firstEngine.TranscribeAsync(new AsrInput("job-001", audioPath), config, options);
        Assert.Contains(firstResult.Segments, segment => segment.RawText == "chunk 2");
        var staleChunkPath = Path.Combine(outputDirectory, "chunks", "chunk-002.json");
        Assert.True(File.Exists(staleChunkPath));

        var secondRunner = new ChunkedAsrProcessRunner(failAtChunk: 2);
        var secondEngine = CreateEngine(paths, secondRunner);
        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() =>
            secondEngine.TranscribeAsync(new AsrInput("job-001", audioPath), config, options));

        Assert.Equal(AsrFailureCategory.ProcessFailed, exception.Category);
        Assert.Contains("chunk 2/3", exception.Message);
        Assert.False(File.Exists(staleChunkPath));
        Assert.Equal(2, secondRunner.Arguments.Count);
        Assert.Equal("failed", ReadLatestRunStatus(paths));
        Assert.Equal("ProcessFailed", ReadLatestRunErrorCategory(paths));
    }

    [Fact]
    public async Task TranscribeAsync_ChunkedGpuAsr_UsesChunkJsonWhenWorkerCrashesAfterWritingOutput()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        var modelPath = Path.Combine(paths.Root, "model");
        File.WriteAllText(scriptPath, "print('ok')");
        WriteSilentPcmWav(audioPath, sampleRate: 10, durationSeconds: 2.4);
        Directory.CreateDirectory(modelPath);
        var runner = new ChunkedAsrProcessRunner(nativeCrashAfterJsonAtChunk: 2);
        var engine = CreateEngine(paths, runner);

        var result = await engine.TranscribeAsync(
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
                ChunkSeconds: 1));

        Assert.Equal(3, result.Segments.Count);
        Assert.Contains(result.Segments, segment => segment.RawText == "chunk 2");
        Assert.Equal("succeeded", ReadSingleRunStatus(paths));
        var chunkLog = File.ReadAllText(Directory.GetFiles(Path.Combine(paths.Jobs, "job-001", "logs"), "asr-*-chunk-002.log").Single());
        Assert.Contains("STATUS_STACK_BUFFER_OVERRUN", chunkLog);
    }

    [Fact]
    public async Task TranscribeAsync_LogsForcedFasterWhisperArguments()
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
                "kotoba-whisper-v2.2-faster",
                scriptPath),
            new AsrOptions());

        Assert.NotNull(runner.Arguments);
        Assert.Contains("--device", runner.Arguments);
        Assert.Contains("auto", runner.Arguments);
        Assert.Contains("--compute-type", runner.Arguments);
        Assert.Contains("float32", runner.Arguments);

        var logPath = Assert.Single(Directory.GetFiles(Path.Combine(paths.Jobs, "job-001", "logs"), "asr-*.log"));
        var log = File.ReadAllText(logPath);
        Assert.Contains("requested_device: auto", log);
        Assert.Contains("requested_compute_type: float32", log);
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
            new JobLogRepository(paths),
            paths);
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

    private static string ReadLatestRunStatus(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM asr_runs ORDER BY created_at DESC, asr_run_id DESC LIMIT 1;";
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

    private static string ReadLatestRunErrorCategory(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT error_category FROM asr_runs ORDER BY created_at DESC, asr_run_id DESC LIMIT 1;";
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

    private static void WriteSilentPcmWav(string path, int sampleRate, double durationSeconds)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var sampleCount = (int)Math.Round(sampleRate * durationSeconds);
        var dataBytes = sampleCount * sizeof(short);
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        for (var index = 0; index < sampleCount; index++)
        {
            writer.Write((short)0);
        }
    }

    private static void DeleteLegacyNvidiaDlls(string asrToolsDirectory)
    {
        foreach (var fileName in new[]
        {
            "cublas64_12.dll",
            "cublasLt64_12.dll",
            "cudart64_12.dll",
            "cudnn64_9.dll"
        })
        {
            var path = Path.Combine(asrToolsDirectory, fileName);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static void WriteNvidiaDllSet(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var fileName in new[]
        {
            "cublas64_12.dll",
            "cublasLt64_12.dll",
            "cudart64_12.dll",
            "cudnn64_9.dll"
        })
        {
            File.WriteAllText(Path.Combine(directory, fileName), "");
        }
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

    private sealed class ChunkedAsrProcessRunner(int? failAtChunk = null, int? nativeCrashAfterJsonAtChunk = null) : ExternalProcessRunner
    {
        public List<IReadOnlyList<string>> Arguments { get; } = [];

        public override Task<ProcessRunResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            IReadOnlyDictionary<string, string>? environment = null)
        {
            Arguments.Add(arguments.ToArray());
            var chunkNumber = Arguments.Count;
            if (failAtChunk == chunkNumber)
            {
                return Task.FromResult(new ProcessRunResult(1, TimeSpan.FromSeconds(1), string.Empty, $"chunk {chunkNumber} failed"));
            }

            var outputJsonIndex = arguments
                .Select((argument, index) => (argument, index))
                .First(item => item.argument == "--output-json")
                .index;
            var outputJsonPath = arguments[outputJsonIndex + 1];
            Directory.CreateDirectory(Path.GetDirectoryName(outputJsonPath)!);
            File.WriteAllText(outputJsonPath, $$"""
                {
                  "segments": [
                    { "start": 0.1, "end": 0.4, "text": "chunk {{chunkNumber}}" }
                  ]
                }
                """);
            if (nativeCrashAfterJsonAtChunk == chunkNumber)
            {
                return Task.FromResult(new ProcessRunResult(
                    -1073740791,
                    TimeSpan.FromSeconds(1),
                    string.Empty,
                    $"chunk {chunkNumber} exited after JSON write"));
            }

            return Task.FromResult(new ProcessRunResult(0, TimeSpan.FromSeconds(1), $"chunk {chunkNumber}", string.Empty));
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
