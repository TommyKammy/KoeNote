using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Services.Review;

public sealed class ReviewWorker(
    ExternalProcessRunner processRunner,
    ReviewCommandBuilder commandBuilder,
    ReviewPromptBuilder promptBuilder,
    ReviewJsonNormalizer normalizer,
    ReviewResultStore resultStore,
    CorrectionDraftRepository repository,
    CorrectionMemoryService? correctionMemoryService = null)
{
    private static readonly SemaphoreSlim ServerLock = new(1, 1);
    private static readonly HttpClient ServerHttpClient = new()
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    private static readonly JsonSerializerOptions ServerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly TimeSpan ServerStopTimeout = TimeSpan.FromSeconds(10);

    private static LlamaServerSession? serverSession;

    public async Task<ReviewRunResult> RunAsync(ReviewRunOptions options, CancellationToken cancellationToken = default)
    {
        ValidateInputs(options);

        var timeout = options.Timeout ?? TimeSpan.FromHours(2);
        var schemaPath = WriteJsonSchema(options.OutputDirectory);
        var chunks = ChunkSegments(options.Segments, options.ChunkSegmentCount).ToArray();
        var allDrafts = new List<CorrectionDraft>();
        var rawOutputs = new List<ReviewChunkRawOutput>();
        var runtimeDiagnostics = new List<string>();
        var totalDuration = TimeSpan.Zero;
        var chunkIndex = 1;

        try
        {
            foreach (var chunk in chunks)
            {
                var chunkResult = await RunChunkAsync(
                    options,
                    chunk,
                    chunkIndex,
                    chunks.Length,
                    schemaPath,
                    timeout,
                    cancellationToken);
                allDrafts.AddRange(chunkResult.Drafts);
                rawOutputs.Add(new ReviewChunkRawOutput(
                    chunkIndex,
                    chunk.First().SegmentId,
                    chunk.Last().SegmentId,
                    chunkResult.RawOutputPath,
                    chunkResult.RawOutput));
                runtimeDiagnostics.AddRange(chunkResult.RuntimeDiagnostics);
                totalDuration += chunkResult.Duration;
                chunkIndex++;
            }

            var rawOutputPath = SaveCombinedRawOutput(options.OutputDirectory, rawOutputs);
            var drafts = MergeMemoryDrafts(options.JobId, options.Segments, allDrafts);
            var normalizedDraftsPath = resultStore.SaveNormalizedDrafts(options.OutputDirectory, drafts);
            repository.ReplaceDrafts(options.JobId, drafts);

            return new ReviewRunResult(
                options.JobId,
                rawOutputPath,
                normalizedDraftsPath,
                drafts,
                totalDuration,
                runtimeDiagnostics);
        }
        finally
        {
            EndRuntimeSession(options);
        }
    }

    private async Task<ReviewChunkResult> RunChunkAsync(
        ReviewRunOptions options,
        IReadOnlyList<TranscriptSegment> segments,
        int chunkIndex,
        int chunkCount,
        string schemaPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var suffix = chunkCount == 1 ? "" : $".chunk-{chunkIndex:D3}";
        var prompt = promptBuilder.Build(segments, options.PromptProfile);
        var promptPath = WritePrompt(options.OutputDirectory, $"review{suffix}.prompt.txt", prompt);
        var processResult = await RunRuntimeAsync(options, promptPath, schemaPath, timeout, cancellationToken);
        var runtimeDiagnostics = new List<string> { processResult.RuntimeDiagnostic.Summary };
        var sanitizedOutput = LlmOutputSanitizer.SanitizeJsonCandidate(processResult.StandardOutput, options.OutputSanitizerProfile);
        var rawOutput = AsrOutputExtractor.ExtractJson(sanitizedOutput, processResult.StandardError);
        var rawOutputPath = resultStore.SaveRawOutput(options.OutputDirectory, rawOutput, $"review{suffix}.raw.json");
        IReadOnlyList<CorrectionDraft> drafts;
        try
        {
            drafts = normalizer.Normalize(options.JobId, segments, rawOutput, options.MinConfidence);
        }
        catch (ReviewWorkerException exception) when (exception.Category == ReviewFailureCategory.JsonParseFailed)
        {
            if (!options.EnableRepair)
            {
                drafts = [];
                return new ReviewChunkResult(rawOutput, rawOutputPath, drafts, processResult.Duration, runtimeDiagnostics);
            }

            var repairPrompt = promptBuilder.BuildRepairPrompt(rawOutput);
            var repairPromptPath = WritePrompt(options.OutputDirectory, $"review{suffix}.repair.prompt.txt", repairPrompt);
            var repairResult = await RunRuntimeAsync(options, repairPromptPath, schemaPath, timeout, cancellationToken);
            runtimeDiagnostics.Add(repairResult.RuntimeDiagnostic.Summary);
            sanitizedOutput = LlmOutputSanitizer.SanitizeJsonCandidate(repairResult.StandardOutput, options.OutputSanitizerProfile);
            rawOutput = AsrOutputExtractor.ExtractJson(sanitizedOutput, repairResult.StandardError);
            rawOutputPath = resultStore.SaveRawOutput(options.OutputDirectory, rawOutput, $"review{suffix}.repair.raw.json");
            try
            {
                drafts = normalizer.Normalize(options.JobId, segments, rawOutput, options.MinConfidence);
            }
            catch (ReviewWorkerException repairException) when (repairException.Category == ReviewFailureCategory.JsonParseFailed)
            {
                drafts = [];
            }

            return new ReviewChunkResult(rawOutput, rawOutputPath, drafts, repairResult.Duration, runtimeDiagnostics);
        }

        return new ReviewChunkResult(rawOutput, rawOutputPath, drafts, processResult.Duration, runtimeDiagnostics);
    }

    private async Task<ReviewRuntimeProcessResult> RunRuntimeAsync(
        ReviewRunOptions options,
        string promptFilePath,
        string jsonSchemaFilePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var schemaPath = options.UseJsonSchema ? jsonSchemaFilePath : null;
        ProcessRunResult processResult;
        try
        {
            processResult = options.UseLlamaServerChatMtp
                ? await RunServerChatMtpAsync(options, promptFilePath, timeout, cancellationToken).ConfigureAwait(false)
                : await RunCompletionAsync(options, promptFilePath, schemaPath, timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (LlamaRuntimePathBridge.IsBridgePreparationException(exception))
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Could not prepare ASCII-safe Review runtime paths: {exception.Message}",
                exception);
        }

        var runtimeDiagnostic = LlamaRuntimeBackendDiagnostics.Analyze(
            options.GpuLayers,
            options.RuntimeEnvironment,
            processResult.StandardError);
        if (runtimeDiagnostic.CudaBackendMissing)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.MissingRuntime,
                $"Review CUDA backend was not loaded even though GPU layers were requested: {runtimeDiagnostic.Summary}");
        }

        if (processResult.ExitCode != 0)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Review runtime exited with code {processResult.ExitCode}: {processResult.StandardError}");
        }

        return new ReviewRuntimeProcessResult(processResult, runtimeDiagnostic);
    }

    private async Task<ProcessRunResult> RunCompletionAsync(
        ReviewRunOptions options,
        string promptFilePath,
        string? schemaPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var pathBridge = LlamaRuntimePathBridge.Create(options.ModelPath);
        var safeOptions = options with { ModelPath = pathBridge.ModelPath };
        var safePromptPath = pathBridge.AddInputFile(promptFilePath);
        var safeSchemaPath = schemaPath is null ? null : pathBridge.AddInputFile(schemaPath);
        var arguments = commandBuilder.BuildArgumentList(safeOptions, safePromptPath, safeSchemaPath);
        return await processRunner.RunAsync(
            options.LlamaCompletionPath,
            arguments,
            timeout,
            cancellationToken,
            options.RuntimeEnvironment).ConfigureAwait(false);
    }

    private static async Task<ProcessRunResult> RunServerChatMtpAsync(
        ReviewRunOptions options,
        string promptPath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var prompt = await File.ReadAllTextAsync(promptPath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        var session = await EnsureServerSessionAsync(options, cancellationToken).ConfigureAwait(false);
        using var request = new HttpRequestMessage(HttpMethod.Post, LlamaTranscriptPolishingRuntime.BuildServerEndpoint(session.BaseUri, "v1/chat/completions"))
        {
            Content = new StringContent(
                BuildServerChatCompletionRequestJson(options, prompt),
                Encoding.UTF8,
                "application/json")
        };

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(timeout);
        var startedAt = DateTimeOffset.UtcNow;
        string responseJson;
        try
        {
            using var response = await ServerHttpClient.SendAsync(request, requestTimeout.Token).ConfigureAwait(false);
            responseJson = await response.Content.ReadAsStringAsync(requestTimeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ReviewWorkerException(
                    ReviewFailureCategory.ProcessFailed,
                    $"Review llama-server request failed with {(int)response.StatusCode}: {responseJson}");
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Review llama-server request timed out after {timeout}.", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Review llama-server request failed: {exception.Message}",
                exception);
        }

        return new ProcessRunResult(
            ExitCode: 0,
            Duration: DateTimeOffset.UtcNow - startedAt,
            StandardOutput: LlamaTranscriptPolishingRuntime.ExtractServerChatCompletionContent(responseJson),
            StandardError: string.Empty);
    }

    internal static string BuildServerChatCompletionRequestJson(
        ReviewRunOptions options,
        string prompt)
    {
        var payload = new LlamaServerChatCompletionRequest(
            Model: options.ModelId,
            Messages: [new LlamaServerChatMessage("user", prompt)],
            MaxTokens: options.MaxTokens,
            Temperature: options.Temperature,
            RepeatPenalty: options.RepeatPenalty,
            Stream: false);

        return JsonSerializer.Serialize(payload, ServerJsonOptions);
    }

    private static async Task<LlamaServerSession> EnsureServerSessionAsync(
        ReviewRunOptions options,
        CancellationToken cancellationToken)
    {
        ValidateServerInputs(options);
        var serverPath = options.LlamaServerPath!;
        var draftPath = options.MtpDraftModelPath!;
        var key = string.Join(
            "\n",
            serverPath,
            options.ModelPath,
            draftPath,
            options.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            options.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            options.MtpDraftGpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            options.MtpDraftTokens.ToString(System.Globalization.CultureInfo.InvariantCulture));

        await ServerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (serverSession is { } existing &&
                existing.Key.Equals(key, StringComparison.Ordinal) &&
                !existing.Process.HasExited)
            {
                return existing;
            }

            StopServerSession();
            ValidateServerCapability(options);
            LlamaRuntimePathBridge? modelPathBridge = null;
            LlamaRuntimePathBridge? draftPathBridge = null;
            var port = GetFreeLoopbackPort();
            var logDirectory = Path.Combine(options.OutputDirectory, "review-llama-server-mtp");
            Directory.CreateDirectory(logDirectory);
            var stderrPath = Path.Combine(logDirectory, "server.stderr.txt");
            var stdoutPath = Path.Combine(logDirectory, "server.stdout.txt");
            Process process;
            ReviewRunOptions safeOptions;
            string safeDraftPath;
            try
            {
                modelPathBridge = LlamaRuntimePathBridge.Create(options.ModelPath);
                draftPathBridge = LlamaRuntimePathBridge.Create(draftPath);
                safeOptions = options with { ModelPath = modelPathBridge.ModelPath };
                safeDraftPath = draftPathBridge.ModelPath;
            }
            catch (Exception exception) when (LlamaRuntimePathBridge.IsBridgePreparationException(exception))
            {
                modelPathBridge?.Dispose();
                draftPathBridge?.Dispose();
                throw new ReviewWorkerException(
                    ReviewFailureCategory.ProcessFailed,
                    $"Could not prepare ASCII-safe Review llama-server runtime paths: {exception.Message}",
                    exception);
            }

            process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = serverPath,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(serverPath) ?? Environment.CurrentDirectory
                },
                EnableRaisingEvents = true
            };

            foreach (var argument in BuildServerArguments(safeOptions, safeDraftPath, port))
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            if (options.RuntimeEnvironment is not null)
            {
                foreach (var item in options.RuntimeEnvironment)
                {
                    process.StartInfo.Environment[item.Key] = item.Value;
                }
            }

            try
            {
                process.Start();
                _ = RedirectToFileAsync(process.StandardOutput, stdoutPath);
                _ = RedirectToFileAsync(process.StandardError, stderrPath);
                var session = new LlamaServerSession(
                    key,
                    process,
                    new Uri($"http://127.0.0.1:{port}"),
                    [modelPathBridge, draftPathBridge]);
                await WaitForServerHealthAsync(
                    session.BaseUri,
                    process,
                    options.Timeout ?? TimeSpan.FromHours(2),
                    cancellationToken).ConfigureAwait(false);
                serverSession = session;
                return session;
            }
            catch
            {
                StopProcess(process);
                modelPathBridge.Dispose();
                draftPathBridge.Dispose();
                throw;
            }
        }
        finally
        {
            ServerLock.Release();
        }
    }

    private static IReadOnlyList<string> BuildServerArguments(
        ReviewRunOptions options,
        string draftPath,
        int port)
    {
        return
        [
            "--model", options.ModelPath,
            "--ctx-size", options.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-gpu-layers", options.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--host", "127.0.0.1",
            "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--spec-type", "draft-mtp",
            "--model-draft", draftPath,
            "--n-gpu-layers-draft", options.MtpDraftGpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--spec-draft-n-max", options.MtpDraftTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--reasoning", "off"
        ];
    }

    private static async Task WaitForServerHealthAsync(
        Uri baseUri,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new ReviewWorkerException(
                    ReviewFailureCategory.ProcessFailed,
                    $"Review llama-server exited before becoming healthy. exit_code={process.ExitCode}");
            }

            try
            {
                using var healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                healthTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await ServerHttpClient.GetAsync(LlamaTranscriptPolishingRuntime.BuildServerEndpoint(baseUri, "health"), healthTimeout.Token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException($"Review llama-server did not become healthy within {timeout}.");
    }

    private static string WritePrompt(string outputDirectory, string fileName, string prompt)
    {
        Directory.CreateDirectory(outputDirectory);
        var promptPath = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(promptPath, prompt, Encoding.UTF8);
        return promptPath;
    }

    private static string WriteJsonSchema(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var schemaPath = Path.Combine(outputDirectory, "review.schema.json");
        File.WriteAllText(schemaPath, """
            {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "segment_id": { "type": "string" },
                  "issue_type": { "type": "string" },
                  "original_text": { "type": "string" },
                  "suggested_text": { "type": "string" },
                  "reason": { "type": "string" },
                  "confidence": { "type": "number" }
                },
                "required": ["segment_id", "issue_type", "original_text", "suggested_text", "reason", "confidence"],
                "additionalProperties": false
              }
            }
            """, Encoding.UTF8);
        return schemaPath;
    }

    private string SaveCombinedRawOutput(string outputDirectory, IReadOnlyList<ReviewChunkRawOutput> rawOutputs)
    {
        if (rawOutputs.Count == 1)
        {
            return rawOutputs[0].RawOutputPath;
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(rawOutputs, new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true
        });
        return resultStore.SaveRawOutput(outputDirectory, payload);
    }

    private static IEnumerable<IReadOnlyList<TranscriptSegment>> ChunkSegments(
        IReadOnlyList<TranscriptSegment> segments,
        int chunkSize)
    {
        for (var index = 0; index < segments.Count; index += chunkSize)
        {
            yield return segments.Skip(index).Take(chunkSize).ToArray();
        }
    }

    private IReadOnlyList<CorrectionDraft> MergeMemoryDrafts(
        string jobId,
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<CorrectionDraft> llmDrafts)
    {
        if (correctionMemoryService is null)
        {
            return llmDrafts;
        }

        var memoryDrafts = correctionMemoryService.BuildMemoryDrafts(jobId, segments);
        if (memoryDrafts.Count == 0)
        {
            return llmDrafts;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var drafts = new List<CorrectionDraft>(memoryDrafts.Count + llmDrafts.Count);
        foreach (var draft in memoryDrafts.Concat(llmDrafts))
        {
            var key = $"{draft.SegmentId}\u001f{draft.OriginalText}\u001f{draft.SuggestedText}";
            if (seen.Add(key))
            {
                drafts.Add(draft);
            }
        }

        return drafts;
    }

    private static void ValidateInputs(ReviewRunOptions options)
    {
        if (!File.Exists(options.LlamaCompletionPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingRuntime, $"Review runtime not found: {options.LlamaCompletionPath}");
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingModel, $"Review model not found: {options.ModelPath}");
        }

        if (options.Segments.Count == 0)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingSegments, "No transcript segments were available for review.");
        }

        if (options.ChunkSegmentCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Chunk segment count must be greater than zero.");
        }
    }

    private static async Task RedirectToFileAsync(StreamReader reader, string path)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            await writer.WriteLineAsync(line).ConfigureAwait(false);
            await writer.FlushAsync().ConfigureAwait(false);
        }
    }

    private static int GetFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static void EndRuntimeSession(ReviewRunOptions options)
    {
        if (options.UseLlamaServerChatMtp)
        {
            ServerLock.Wait();
            try
            {
                StopServerSession();
            }
            finally
            {
                ServerLock.Release();
            }
        }
    }

    private static void StopServerSession()
    {
        if (serverSession is not { } existing)
        {
            return;
        }

        StopProcess(existing.Process);
        foreach (var pathBridge in existing.PathBridges)
        {
            pathBridge.Dispose();
        }

        serverSession = null;
    }

    private static void StopProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit((int)ServerStopTimeout.TotalMilliseconds);
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static void ValidateServerInputs(ReviewRunOptions options)
    {
        if (!File.Exists(options.LlamaServerPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingRuntime, $"llama-server runtime not found: {options.LlamaServerPath}");
        }

        if (!File.Exists(options.MtpDraftModelPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingModel, $"Gemma 12B MTP draft model not found: {options.MtpDraftModelPath}");
        }
    }

    private static void ValidateServerCapability(ReviewRunOptions options)
    {
        if (!Gemma12BLocalValidation.IsLlamaServerMtpCapable(options.LlamaServerPath!, options.RuntimeEnvironment))
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"llama-server runtime does not support Gemma 12B MTP options: {options.LlamaServerPath}");
        }
    }

    private sealed record ReviewChunkResult(
        string RawOutput,
        string RawOutputPath,
        IReadOnlyList<CorrectionDraft> Drafts,
        TimeSpan Duration,
        IReadOnlyList<string> RuntimeDiagnostics);

    private sealed record ReviewRuntimeProcessResult(
        ProcessRunResult ProcessResult,
        LlamaRuntimeBackendDiagnostic RuntimeDiagnostic)
    {
        public int ExitCode => ProcessResult.ExitCode;

        public TimeSpan Duration => ProcessResult.Duration;

        public string StandardOutput => ProcessResult.StandardOutput;

        public string StandardError => ProcessResult.StandardError;
    }

    private sealed record ReviewChunkRawOutput(
        int ChunkIndex,
        string FirstSegmentId,
        string LastSegmentId,
        string RawOutputPath,
        string RawOutput);

    private sealed record LlamaServerSession(
        string Key,
        Process Process,
        Uri BaseUri,
        IReadOnlyList<IDisposable> PathBridges);

    private sealed record LlamaServerChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<LlamaServerChatMessage> Messages,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("temperature")] double Temperature,
        [property: JsonPropertyName("repeat_penalty")] double? RepeatPenalty,
        [property: JsonPropertyName("stream")] bool Stream);

    private sealed record LlamaServerChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string? Content);
}
