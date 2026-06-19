using System.IO;
using System.Text.Json;
using KoeNote.App.Models;
using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Services.Asr;

public sealed class ScriptedJsonAsrEngine(
    string engineId,
    string displayName,
    string sourceName,
    ExternalProcessRunner processRunner,
    AsrJsonNormalizer normalizer,
    AsrResultStore resultStore,
    TranscriptSegmentRepository transcriptSegmentRepository,
    AsrRunRepository asrRunRepository,
    JobLogRepository jobLogRepository,
    AppPaths paths) : IAsrEngine
{
    public string EngineId => engineId;

    public string DisplayName => displayName;

    public Task<AsrEngineCheckResult> CheckAsync(
        AsrEngineConfig config,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        if (string.IsNullOrWhiteSpace(config.WorkerPath) || !File.Exists(config.WorkerPath))
        {
            messages.Add($"Missing ASR worker script: {config.WorkerPath}");
        }

        if (!AsrWorkerCommandBuilder.IsCommandName(config.RuntimePath) && !File.Exists(config.RuntimePath))
        {
            messages.Add($"Missing ASR runtime: {config.RuntimePath}");
        }

        if (!File.Exists(config.ModelPath) && !Directory.Exists(config.ModelPath))
        {
            messages.Add($"Missing ASR model: {config.ModelPath}");
        }

        return Task.FromResult(new AsrEngineCheckResult(messages.Count == 0, messages));
    }

    public async Task<AsrResult> TranscribeAsync(
        AsrInput input,
        AsrEngineConfig config,
        AsrOptions options,
        CancellationToken cancellationToken = default)
    {
        var asrRunId = asrRunRepository.Start(input.JobId, EngineId, config.ModelId, config.ModelVersion);
        var scriptJsonPath = Path.Combine(config.OutputDirectory, $"{EngineId}.json");
        var timeout = options.Timeout ?? TimeSpan.FromHours(2);

        try
        {
            ValidateInputs(input, config);
            Directory.CreateDirectory(config.OutputDirectory);
        var processEnvironment = AsrWorkerProcessEnvironmentBuilder.Build(config, paths);
            var chunks = new WavAudioChunker().SplitLongWav(
                input.NormalizedAudioPath,
                Path.Combine(config.OutputDirectory, "chunks"),
                options.ChunkSeconds.GetValueOrDefault());
            if (chunks.Count > 0)
            {
                return await TranscribeChunksAsync(
                    input,
                    config,
                    options,
                    asrRunId,
                    timeout,
                    processEnvironment,
                    chunks,
                    cancellationToken);
            }

        var arguments = AsrWorkerCommandBuilder.BuildArguments(input, config, options, scriptJsonPath, ShouldDisableWorkerDiarization());
            var processResult = await processRunner.RunAsync(
                config.RuntimePath,
                arguments,
                timeout,
                cancellationToken,
                processEnvironment.Environment);
            var workerLogPath = SaveAsrWorkerLog(
                input,
                config,
                asrRunId,
                arguments,
                options,
                scriptJsonPath,
                processResult,
                processEnvironment);
            if (processResult.ExitCode != 0)
            {
                if (!File.Exists(scriptJsonPath))
                {
                    var workerOutput = string.IsNullOrWhiteSpace(processResult.StandardError)
                        ? processResult.StandardOutput
                        : processResult.StandardError;
            var category = AsrWorkerFailureClassifier.ClassifyProcessFailure(processResult.ExitCode, workerOutput);
            var exitSummary = AsrWorkerFailureClassifier.DescribeExitCode(processResult.ExitCode);
                    throw new AsrWorkerException(
                        category,
                        $"{DisplayName} exited with code {processResult.ExitCode} ({exitSummary}): {processResult.StandardError} Worker log: {workerLogPath}",
                        workerLogPath: workerLogPath);
                }
            }

            var rawOutput = File.Exists(scriptJsonPath)
                ? File.ReadAllText(scriptJsonPath, System.Text.Encoding.UTF8)
                : AsrOutputExtractor.ExtractJson(processResult.StandardOutput, processResult.StandardError);
            var rawOutputPath = resultStore.SaveRawOutput(config.OutputDirectory, rawOutput);
            var segments = normalizer.Normalize(input.JobId, rawOutput)
                .Select(segment => segment with { Source = sourceName, AsrRunId = asrRunId })
                .ToArray();
            var normalizedSegmentsPath = resultStore.SaveNormalizedSegments(config.OutputDirectory, segments);
            transcriptSegmentRepository.ReplaceSegments(input.JobId, segments);
            asrRunRepository.MarkSucceeded(asrRunId, processResult.Duration, rawOutputPath, normalizedSegmentsPath);

            return new AsrResult(asrRunId, input.JobId, rawOutputPath, normalizedSegmentsPath, segments, processResult.Duration);
        }
        catch (AsrWorkerException exception)
        {
            asrRunRepository.MarkFailed(asrRunId, exception.Category.ToString());
            throw;
        }
        catch (OperationCanceledException)
        {
            asrRunRepository.MarkCancelled(asrRunId);
            throw;
        }
        catch
        {
            asrRunRepository.MarkFailed(asrRunId, AsrFailureCategory.Unknown.ToString());
            throw;
        }
    }

    private async Task<AsrResult> TranscribeChunksAsync(
        AsrInput input,
        AsrEngineConfig config,
        AsrOptions options,
        string asrRunId,
        TimeSpan timeout,
        AsrProcessEnvironment processEnvironment,
        IReadOnlyList<WavAudioChunk> chunks,
        CancellationToken cancellationToken)
    {
        var workerOptions = options with { ChunkSeconds = null };
        var segments = new List<TranscriptSegment>();
        var totalDuration = TimeSpan.Zero;
        jobLogRepository.AddEvent(
            input.JobId,
            "asr",
            "info",
            $"Chunked GPU ASR enabled: {chunks.Count} chunks, up to {options.ChunkSeconds} seconds each.");

        foreach (var chunk in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunkInput = input with { NormalizedAudioPath = chunk.AudioPath };
            var chunkJsonPath = Path.Combine(config.OutputDirectory, "chunks", $"chunk-{chunk.Index:D3}.json");
            var chunkContext = new AsrChunkLogContext(
                chunk.Index,
                chunks.Count,
                chunk.OffsetSeconds,
                chunk.DurationSeconds,
                chunk.AudioPath);
            var arguments = AsrWorkerCommandBuilder.BuildArguments(chunkInput, config, workerOptions, chunkJsonPath, ShouldDisableWorkerDiarization());
            jobLogRepository.AddEvent(
                input.JobId,
                "asr",
                "info",
                $"Running ASR chunk {chunk.Index}/{chunks.Count} at {chunk.OffsetSeconds:F1}s...");
            if (File.Exists(chunkJsonPath))
            {
                File.Delete(chunkJsonPath);
            }

            var processResult = await processRunner.RunAsync(
                config.RuntimePath,
                arguments,
                timeout,
                cancellationToken,
                processEnvironment.Environment);
            totalDuration += processResult.Duration;
            var workerLogPath = SaveAsrWorkerLog(
                chunkInput,
                config,
                asrRunId,
                arguments,
                workerOptions,
                chunkJsonPath,
                processResult,
                processEnvironment,
                chunkContext);

            if (processResult.ExitCode != 0 && !File.Exists(chunkJsonPath))
            {
                var workerOutput = string.IsNullOrWhiteSpace(processResult.StandardError)
                    ? processResult.StandardOutput
                    : processResult.StandardError;
                var category = AsrWorkerFailureClassifier.ClassifyProcessFailure(processResult.ExitCode, workerOutput);
                var exitSummary = AsrWorkerFailureClassifier.DescribeExitCode(processResult.ExitCode);
                throw new AsrWorkerException(
                    category,
                    $"{DisplayName} chunk {chunk.Index}/{chunks.Count} exited with code {processResult.ExitCode} ({exitSummary}): {processResult.StandardError} Worker log: {workerLogPath}",
                    workerLogPath: workerLogPath);
            }

            var rawOutput = File.Exists(chunkJsonPath)
                ? File.ReadAllText(chunkJsonPath, System.Text.Encoding.UTF8)
                : AsrOutputExtractor.ExtractJson(processResult.StandardOutput, processResult.StandardError);
            IReadOnlyList<TranscriptSegment> chunkSegments;
            try
            {
                chunkSegments = normalizer.Normalize(input.JobId, rawOutput);
            }
            catch (AsrWorkerException exception) when (exception.Category == AsrFailureCategory.NoSegments)
            {
                jobLogRepository.AddEvent(
                    input.JobId,
                    "asr",
                    "warning",
                    $"ASR chunk {chunk.Index}/{chunks.Count} had no usable segments. Worker log: {workerLogPath}");
                continue;
            }
            catch (AsrWorkerException exception)
            {
                throw new AsrWorkerException(
                    exception.Category,
                    $"{DisplayName} chunk {chunk.Index}/{chunks.Count} output failed validation: {exception.Message} Worker log: {workerLogPath}",
                    exception,
                    workerLogPath);
            }

            foreach (var segment in chunkSegments)
            {
                var segmentId = (segments.Count + 1).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
                segments.Add(segment with
                {
                    SegmentId = segmentId,
                    StartSeconds = segment.StartSeconds + chunk.OffsetSeconds,
                    EndSeconds = segment.EndSeconds + chunk.OffsetSeconds,
                    Source = sourceName,
                    AsrRunId = asrRunId
                });
            }

            jobLogRepository.AddEvent(
                input.JobId,
                "asr",
                "info",
                $"ASR chunk {chunk.Index}/{chunks.Count} completed: {chunkSegments.Count} segments. Worker log: {workerLogPath}");
        }

        if (segments.Count == 0)
        {
            throw new AsrWorkerException(AsrFailureCategory.NoSegments, "Chunked ASR did not produce any usable segments.");
        }

        var mergedRawOutput = SerializeMergedRawOutput(segments);
        var rawOutputPath = resultStore.SaveRawOutput(config.OutputDirectory, mergedRawOutput);
        var normalizedSegmentsPath = resultStore.SaveNormalizedSegments(config.OutputDirectory, segments);
        transcriptSegmentRepository.ReplaceSegments(input.JobId, segments);
        asrRunRepository.MarkSucceeded(asrRunId, totalDuration, rawOutputPath, normalizedSegmentsPath);
        jobLogRepository.AddEvent(
            input.JobId,
            "asr",
            "info",
            $"Chunked GPU ASR merged {segments.Count} segments from {chunks.Count} chunks.");

        return new AsrResult(asrRunId, input.JobId, rawOutputPath, normalizedSegmentsPath, segments, totalDuration);
    }

    private bool ShouldDisableWorkerDiarization()
    {
        return sourceName.Equals("faster-whisper", StringComparison.OrdinalIgnoreCase);
    }

    private string SaveAsrWorkerLog(
        AsrInput input,
        AsrEngineConfig config,
        string asrRunId,
        IReadOnlyList<string> arguments,
        AsrOptions options,
        string outputJsonPath,
        ProcessRunResult processResult,
        AsrProcessEnvironment processEnvironment,
        AsrChunkLogContext? chunkContext = null)
    {
        var requestedDevice = AsrWorkerCommandBuilder.GetArgumentValue(arguments, "--device", options.Device ?? "(unset)");
        var requestedComputeType = AsrWorkerCommandBuilder.GetArgumentValue(arguments, "--compute-type", options.ComputeType ?? "(unset)");
        var metadata = new Dictionary<string, string>
        {
            ["engine_id"] = EngineId,
            ["display_name"] = DisplayName,
            ["runtime_path"] = config.RuntimePath,
            ["worker_script_path"] = config.WorkerPath ?? "(unset)",
            ["argument_summary"] = AsrWorkerCommandBuilder.BuildArgumentSummary(arguments),
            ["model_id"] = config.ModelId,
            ["model_version"] = config.ModelVersion ?? "(unset)",
            ["requested_device"] = requestedDevice,
            ["requested_compute_type"] = requestedComputeType,
            ["execution_profile_id"] = options.ExecutionProfileId ?? "(unset)",
            ["attempt_number"] = Math.Max(1, options.AttemptNumber).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["chunk_seconds"] = options.ChunkSeconds?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(unset)",
            ["model_path"] = config.ModelPath,
            ["normalized_audio_path"] = input.NormalizedAudioPath,
            ["output_json_path"] = outputJsonPath,
            ["exit_code"] = processResult.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["exit_summary"] = AsrWorkerFailureClassifier.DescribeExitCode(processResult.ExitCode),
            ["duration"] = processResult.Duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture),
            ["asr_path_entries"] = processEnvironment.AddedPathEntries.Count == 0
                ? "(none)"
                : string.Join(";", processEnvironment.AddedPathEntries),
            ["koenote_asr_tools_dir"] = processEnvironment.Environment.TryGetValue("KOENOTE_ASR_TOOLS_DIR", out var asrToolsDir)
                ? asrToolsDir
                : "(unset)",
            ["koenote_ctranslate2_cuda_dir"] = processEnvironment.Environment.TryGetValue("KOENOTE_CTRANSLATE2_CUDA_DIR", out var ctranslate2CudaDir)
                ? ctranslate2CudaDir
                : "(unset)"
        };
        var logFileName = $"asr-{asrRunId}.log";
        if (chunkContext is not null)
        {
            metadata["chunk_index"] = chunkContext.Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["chunk_count"] = chunkContext.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            metadata["chunk_offset_seconds"] = chunkContext.OffsetSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            metadata["chunk_duration_seconds"] = chunkContext.DurationSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            metadata["chunk_audio_path"] = chunkContext.AudioPath;
            logFileName = $"asr-{asrRunId}-chunk-{chunkContext.Index:D3}.log";
        }

        return jobLogRepository.SaveWorkerLog(
            input.JobId,
            "asr",
            processResult.StandardOutput,
            processResult.StandardError,
            metadata,
            logFileName);
    }

    private static string SerializeMergedRawOutput(IReadOnlyList<TranscriptSegment> segments)
    {
        var payload = new
        {
            segments = segments.Select(segment => new
            {
                segment_id = segment.SegmentId,
                start = segment.StartSeconds,
                end = segment.EndSeconds,
                speaker_id = segment.SpeakerId,
                text = segment.RawText,
                asr_confidence = segment.AsrConfidence
            })
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ValidateInputs(AsrInput input, AsrEngineConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WorkerPath) || !File.Exists(config.WorkerPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingRuntime, $"ASR worker script not found: {config.WorkerPath}");
        }

        if (!AsrWorkerCommandBuilder.IsCommandName(config.RuntimePath) && !File.Exists(config.RuntimePath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingRuntime, $"ASR runtime not found: {config.RuntimePath}");
        }

        if (!File.Exists(config.ModelPath) && !Directory.Exists(config.ModelPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingModel, $"ASR model not found: {config.ModelPath}");
        }

        if (!File.Exists(input.NormalizedAudioPath))
        {
            throw new AsrWorkerException(AsrFailureCategory.MissingAudio, $"Normalized audio not found: {input.NormalizedAudioPath}");
        }
    }

    private sealed record AsrChunkLogContext(
        int Index,
        int Count,
        double OffsetSeconds,
        double DurationSeconds,
        string AudioPath);
}
