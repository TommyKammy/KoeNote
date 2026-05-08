using System.IO;
using System.Text;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Presets;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed class LlamaTranscriptSummaryRuntime(
    ExternalProcessRunner processRunner,
    TranscriptSummaryPromptBuilder promptBuilder,
    DomainPromptContextProvider? domainPromptContextProvider = null)
    : ITranscriptSummaryRuntime
{
    public async Task<TranscriptSummaryChunkResult> SummarizeChunkAsync(
        TranscriptSummaryOptions options,
        TranscriptSummaryChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(options);
        if (string.IsNullOrWhiteSpace(chunk.Content))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingSegments, "No transcript content was available for summary.");
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var prompt = promptBuilder.BuildChunkPrompt(
            chunk,
            options.ModelId,
            LoadDomainContext(options.ModelId, chunk.Content),
            options.PromptTemplateId,
            options.Attempt);
        var promptPath = Path.Combine(
            options.OutputDirectory,
            options.Attempt <= 1
                ? $"summary.chunk-{chunk.ChunkIndex:D3}.prompt.txt"
                : $"summary.chunk-{chunk.ChunkIndex:D3}.retry-{options.Attempt:D3}.prompt.txt");
        await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8, cancellationToken);

        var start = DateTimeOffset.UtcNow;
        var result = await RunAsync(options, promptPath, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, $"summary.chunk-{chunk.ChunkIndex:D3}.raw.md"),
            result.StandardOutput,
            Encoding.UTF8,
            cancellationToken);
        var content = LlmOutputSanitizer.SanitizeMarkdown(result.StandardOutput, options.OutputSanitizerProfile);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.JsonParseFailed, "Transcript summary returned empty output.");
        }

        var outputPath = Path.Combine(options.OutputDirectory, $"summary.chunk-{chunk.ChunkIndex:D3}.md");
        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, cancellationToken);
        return new TranscriptSummaryChunkResult(chunk, content, DateTimeOffset.UtcNow - start);
    }

    public async Task<string> MergeSummariesAsync(
        TranscriptSummaryOptions options,
        IReadOnlyList<TranscriptSummaryChunkResult> chunkResults,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(options);
        if (chunkResults.Count == 0)
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingSegments, "No chunk summaries were available for final summary.");
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var prompt = promptBuilder.BuildFinalPrompt(
            chunkResults,
            options.ModelId,
            LoadDomainContext(options.ModelId, BuildFinalSummarySourceText(chunkResults)),
            options.PromptTemplateId,
            options.Attempt);
        var promptPath = Path.Combine(
            options.OutputDirectory,
            options.Attempt <= 1 ? "summary.final.prompt.txt" : $"summary.final.retry-{options.Attempt:D3}.prompt.txt");
        await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8, cancellationToken);

        var result = await RunAsync(options, promptPath, cancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(options.OutputDirectory, "summary.final.raw.md"),
            result.StandardOutput,
            Encoding.UTF8,
            cancellationToken);
        var content = LlmOutputSanitizer.SanitizeMarkdown(result.StandardOutput, options.OutputSanitizerProfile);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.JsonParseFailed, "Final transcript summary returned empty output.");
        }

        await File.WriteAllTextAsync(Path.Combine(options.OutputDirectory, "summary.final.md"), content, Encoding.UTF8, cancellationToken);
        return content;
    }

    private async Task<ProcessRunResult> RunAsync(
        TranscriptSummaryOptions options,
        string promptPath,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(
            options.LlamaCompletionPath,
            BuildArguments(options, promptPath),
            options.Timeout ?? TimeSpan.FromHours(2),
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Transcript summary runtime exited with code {result.ExitCode}: {result.StandardError}");
        }

        return result;
    }

    private DomainPromptContext? LoadDomainContext(string? modelId, string? sourceText)
    {
        return domainPromptContextProvider?.LoadForSummary(sourceText, ResolveDomainContextLimits(modelId));
    }

    private static DomainPromptContextLimits ResolveDomainContextLimits(string? modelId)
    {
        return IsBonsaiModel(modelId)
            ? DomainPromptContextLimits.BonsaiSummary
            : DomainPromptContextLimits.SummaryDefault;
    }

    private static bool IsBonsaiModel(string? modelId)
    {
        return !string.IsNullOrWhiteSpace(modelId) &&
            modelId.Contains("bonsai", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildFinalSummarySourceText(IReadOnlyList<TranscriptSummaryChunkResult> chunkResults)
    {
        return string.Join(Environment.NewLine, chunkResults.Select(static result => result.Content));
    }

    private static IReadOnlyList<string> BuildArguments(TranscriptSummaryOptions options, string promptPath)
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

    private static void ValidateInputs(TranscriptSummaryOptions options)
    {
        if (!File.Exists(options.LlamaCompletionPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingRuntime, $"Review runtime not found: {options.LlamaCompletionPath}");
        }

        if (!File.Exists(options.ModelPath))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.MissingModel, $"Review model not found: {options.ModelPath}");
        }
    }
}
