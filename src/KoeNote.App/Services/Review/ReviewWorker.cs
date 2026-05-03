using System.IO;
using KoeNote.App.Models;
using KoeNote.App.Services.Asr;

namespace KoeNote.App.Services.Review;

public sealed class ReviewWorker(
    ExternalProcessRunner processRunner,
    ReviewCommandBuilder commandBuilder,
    ReviewPromptBuilder promptBuilder,
    ReviewJsonNormalizer normalizer,
    ReviewResultStore resultStore,
    CorrectionDraftRepository repository)
{
    public async Task<ReviewRunResult> RunAsync(ReviewRunOptions options, CancellationToken cancellationToken = default)
    {
        ValidateInputs(options);

        var timeout = options.Timeout ?? TimeSpan.FromHours(2);
        var prompt = promptBuilder.Build(options.Segments);
        var processResult = await RunRuntimeAsync(options, prompt, timeout, cancellationToken);

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
            var repairResult = await RunRuntimeAsync(options, repairPrompt, timeout, cancellationToken);
            rawOutput = AsrOutputExtractor.ExtractJson(repairResult.StandardOutput, repairResult.StandardError);
            rawOutputPath = resultStore.SaveRawOutput(options.OutputDirectory, rawOutput, "review.repair.raw.json");
            drafts = normalizer.Normalize(options.JobId, options.Segments, rawOutput, options.MinConfidence);
            processResult = repairResult;
        }

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
        string prompt,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var arguments = commandBuilder.BuildArgumentList(options, prompt);
        var processResult = await processRunner.RunAsync(options.LlamaCompletionPath, arguments, timeout, cancellationToken);

        if (processResult.ExitCode != 0)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Review runtime exited with code {processResult.ExitCode}: {processResult.StandardError}");
        }

        return processResult;
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
