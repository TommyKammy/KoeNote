using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed class LlamaTranscriptPolishingRuntime(
    ExternalProcessRunner processRunner,
    TranscriptPolishingPromptBuilder promptBuilder)
    : ITranscriptPolishingRuntime, ITranscriptPolishingRuntimeSession
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

    public void EndPolishingSession(TranscriptPolishingOptions options)
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

    public async Task<TranscriptPolishingChunkResult> PolishChunkAsync(
        TranscriptPolishingOptions options,
        TranscriptPolishingChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(options, chunk);
        var timeout = options.Timeout ?? TimeSpan.FromHours(2);
        Directory.CreateDirectory(options.OutputDirectory);

        var prompt = promptBuilder.Build(chunk, options.PromptTemplateId, options.PromptSettings);
        var promptPath = Path.Combine(options.OutputDirectory, $"polish.chunk-{chunk.ChunkIndex:D3}.prompt.txt");
        await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8, cancellationToken);

        var start = DateTimeOffset.UtcNow;
        if (options.UseLlamaServerChatMtp)
        {
            return await PolishChunkWithServerChatMtpAsync(
                options,
                chunk,
                prompt,
                start,
                cancellationToken);
        }

        ProcessRunResult result;
        try
        {
            using var pathBridge = LlamaRuntimePathBridge.Create(options.ModelPath);
            var safePromptPath = pathBridge.AddInputFile(promptPath);
            var safeOptions = options with { ModelPath = pathBridge.ModelPath };
            result = await processRunner.RunAsync(
                options.LlamaCompletionPath,
                BuildArguments(safeOptions, safePromptPath),
                timeout,
                cancellationToken,
                options.RuntimeEnvironment);
        }
        catch (Exception exception) when (LlamaRuntimePathBridge.IsBridgePreparationException(exception))
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Could not prepare ASCII-safe transcript polishing runtime paths: {exception.Message}",
                exception);
        }

        if (result.ExitCode != 0)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Transcript polishing runtime exited with code {result.ExitCode}: {result.StandardError}");
        }

        var content = LlmOutputSanitizer.SanitizeMarkdown(result.StandardOutput, options.OutputSanitizerProfile);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.JsonParseFailed, "Transcript polishing returned empty output.");
        }

        var outputPath = Path.Combine(options.OutputDirectory, $"polish.chunk-{chunk.ChunkIndex:D3}.txt");
        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, cancellationToken);
        return new TranscriptPolishingChunkResult(chunk, content, DateTimeOffset.UtcNow - start);
    }

    private async Task<TranscriptPolishingChunkResult> PolishChunkWithServerChatMtpAsync(
        TranscriptPolishingOptions options,
        TranscriptPolishingChunk chunk,
        string prompt,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        var session = await EnsureServerSessionAsync(options, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildServerEndpoint(session.BaseUri, "v1/chat/completions"))
        {
            Content = new StringContent(
                BuildServerChatCompletionRequestJson(options, prompt),
                Encoding.UTF8,
                "application/json")
        };

        var timeout = options.Timeout ?? TimeSpan.FromHours(2);
        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(timeout);

        string responseJson;
        try
        {
            using var response = await ServerHttpClient.SendAsync(request, requestTimeout.Token).ConfigureAwait(false);
            responseJson = await response.Content.ReadAsStringAsync(requestTimeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new ReviewWorkerException(
                    ReviewFailureCategory.ProcessFailed,
                    $"Transcript polishing llama-server request failed with {(int)response.StatusCode}: {responseJson}");
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Transcript polishing llama-server request timed out after {timeout}.", exception);
        }

        var content = ExtractServerChatCompletionContent(responseJson);

        var sanitized = LlmOutputSanitizer.SanitizeMarkdown(content, options.OutputSanitizerProfile);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.JsonParseFailed, "Transcript polishing returned empty output.");
        }

        var outputPath = Path.Combine(options.OutputDirectory, $"polish.chunk-{chunk.ChunkIndex:D3}.txt");
        await File.WriteAllTextAsync(outputPath, sanitized, Encoding.UTF8, cancellationToken);
        return new TranscriptPolishingChunkResult(chunk, sanitized, DateTimeOffset.UtcNow - start);
    }

    private static async Task<LlamaServerSession> EnsureServerSessionAsync(
        TranscriptPolishingOptions options,
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
            var logDirectory = Path.Combine(options.OutputDirectory, "llama-server-mtp");
            Directory.CreateDirectory(logDirectory);
            var stderrPath = Path.Combine(logDirectory, "server.stderr.txt");
            var stdoutPath = Path.Combine(logDirectory, "server.stdout.txt");
            Process process;
            TranscriptPolishingOptions safeOptions;
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
                    $"Could not prepare ASCII-safe llama-server runtime paths: {exception.Message}",
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
        TranscriptPolishingOptions options,
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
                    $"llama-server exited before becoming healthy. exit_code={process.ExitCode}");
            }

            try
            {
                using var healthTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                healthTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var response = await ServerHttpClient.GetAsync(BuildServerEndpoint(baseUri, "health"), healthTimeout.Token).ConfigureAwait(false);
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

        throw new TimeoutException($"llama-server did not become healthy within {timeout}.");
    }

    internal static Uri BuildServerEndpoint(Uri baseUri, string relativePath)
    {
        return new Uri(baseUri, relativePath);
    }

    internal static string BuildServerChatCompletionRequestJson(
        TranscriptPolishingOptions options,
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

    internal static string ExtractServerChatCompletionContent(string responseJson)
    {
        LlamaServerChatCompletionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<LlamaServerChatCompletionResponse>(
                responseJson,
                ServerJsonOptions);
        }
        catch (JsonException exception)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.JsonParseFailed,
                "Transcript polishing llama-server returned malformed JSON.",
                exception);
        }

        var message = parsed?.Choices?.FirstOrDefault()?.Message;
        if (string.IsNullOrWhiteSpace(message?.Content))
        {
            var reasoningLength = message?.ReasoningContent?.Length ?? 0;
            throw new ReviewWorkerException(
                ReviewFailureCategory.JsonParseFailed,
                $"Transcript polishing llama-server returned empty content. reasoning_content_length={reasoningLength}.");
        }

        return message.Content;
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

    private static IReadOnlyList<string> BuildArguments(TranscriptPolishingOptions options, string promptPath)
    {
        return LlamaCompletionArgumentBuilder.Build(new LlamaCompletionArgumentOptions(
            options.ModelPath,
            promptPath,
            options.ContextSize,
            options.GpuLayers,
            options.MaxTokens,
            options.Temperature,
            options.Threads,
            options.ThreadsBatch,
            options.NoConversation,
            options.TopP,
            options.TopK,
            options.RepeatPenalty));
    }

    private static void ValidateInputs(TranscriptPolishingOptions options, TranscriptPolishingChunk chunk)
    {
        if (!File.Exists(options.LlamaCompletionPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingRuntime, $"Review runtime not found: {options.LlamaCompletionPath}");
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingModel, $"Review model not found: {options.ModelPath}");
        }

        if (chunk.Segments.Count == 0)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingSegments, "No transcript segments were available for polishing.");
        }
    }

    private static void ValidateServerInputs(TranscriptPolishingOptions options)
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

    private static void ValidateServerCapability(TranscriptPolishingOptions options)
    {
        if (!Gemma12BLocalValidation.IsLlamaServerMtpCapable(options.LlamaServerPath!, options.RuntimeEnvironment))
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"llama-server runtime does not support Gemma 12B MTP options: {options.LlamaServerPath}");
        }
    }

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
        [property: JsonPropertyName("content")] string? Content)
    {
        [JsonPropertyName("reasoning_content")]
        public string? ReasoningContent { get; init; }
    }

    private sealed record LlamaServerChatChoice(
        [property: JsonPropertyName("message")] LlamaServerChatMessage? Message);

    private sealed record LlamaServerChatCompletionResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<LlamaServerChatChoice>? Choices);
}
