using System.IO;
using System.Text;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

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
        var prompt = promptBuilder.Build(options.Segments);
        var promptPath = WritePrompt(options.OutputDirectory, "review.prompt.txt", prompt);
        var schemaPath = WriteJsonSchema(options.OutputDirectory);
        var processResult = await RunRuntimeAsync(options, promptPath, schemaPath, timeout, cancellationToken);

        var rawOutput = AsrOutputExtractor.ExtractJson(processResult.StandardOutput, processResult.StandardError);
        var rawOutputPath = resultStore.SaveRawOutput(options.OutputDirectory, rawOutput);
        IReadOnlyList<CorrectionDraft> drafts;
        try
        {
            drafts = normalizer.Normalize(options.JobId, options.Segments, rawOutput, options.MinConfidence);
        }
        catch (ReviewWorkerException exception) when (exception.Category == ReviewFailureCategory.JsonParseFailed)
        {
            var repairPrompt = promptBuilder.BuildRepairPrompt(rawOutput);
            var repairPromptPath = WritePrompt(options.OutputDirectory, "review.repair.prompt.txt", repairPrompt);
            var repairResult = await RunRuntimeAsync(options, repairPromptPath, schemaPath, timeout, cancellationToken);
            rawOutput = AsrOutputExtractor.ExtractJson(repairResult.StandardOutput, repairResult.StandardError);
            rawOutputPath = resultStore.SaveRawOutput(options.OutputDirectory, rawOutput, "review.repair.raw.json");
            try
            {
                drafts = normalizer.Normalize(options.JobId, options.Segments, rawOutput, options.MinConfidence);
            }
            catch (ReviewWorkerException repairException) when (repairException.Category == ReviewFailureCategory.JsonParseFailed)
            {
                drafts = [];
            }

            processResult = repairResult;
        }

        drafts = MergeMemoryDrafts(options.JobId, options.Segments, drafts);
        var normalizedDraftsPath = resultStore.SaveNormalizedDrafts(options.OutputDirectory, drafts);
        repository.ReplaceDrafts(options.JobId, drafts);

        return new ReviewRunResult(
            options.JobId,
            rawOutputPath,
            normalizedDraftsPath,
            drafts,
            processResult.Duration);
    }

    private async Task<ProcessRunResult> RunRuntimeAsync(
        ReviewRunOptions options,
        string promptFilePath,
        string jsonSchemaFilePath,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = commandBuilder.BuildArgumentList(options, promptFilePath, jsonSchemaFilePath);
        var processResult = await processRunner.RunAsync(options.LlamaCompletionPath, arguments, timeout, cancellationToken);

        if (processResult.ExitCode != 0)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Review runtime exited with code {processResult.ExitCode}: {processResult.StandardError}");
        }

        return processResult;
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
    }
}
