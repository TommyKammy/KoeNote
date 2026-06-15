using System.IO;
using System.Text;
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
            using var pathBridge = LlamaRuntimePathBridge.Create(options.ModelPath);
            var safeOptions = options with { ModelPath = pathBridge.ModelPath };
            var safePromptPath = pathBridge.AddInputFile(promptFilePath);
            var safeSchemaPath = schemaPath is null ? null : pathBridge.AddInputFile(schemaPath);
            var arguments = commandBuilder.BuildArgumentList(safeOptions, safePromptPath, safeSchemaPath);
            processResult = await processRunner.RunAsync(
                options.LlamaCompletionPath,
                arguments,
                timeout,
                cancellationToken,
                options.RuntimeEnvironment);
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
}
