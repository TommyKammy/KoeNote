using System.IO;
using System.Text;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed class LlamaTranscriptSummaryRuntime(
    ExternalProcessRunner processRunner,
    TranscriptSummaryPromptBuilder promptBuilder)
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
        var prompt = promptBuilder.BuildChunkPrompt(chunk, options.ModelId);
        var promptPath = Path.Combine(options.OutputDirectory, $"summary.chunk-{chunk.ChunkIndex:D3}.prompt.txt");
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
        var prompt = promptBuilder.BuildFinalPrompt(chunkResults, options.ModelId);
        var promptPath = Path.Combine(options.OutputDirectory, "summary.final.prompt.txt");
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

    private static IReadOnlyList<string> BuildArguments(TranscriptSummaryOptions options, string promptPath)
    {
        var arguments = new List<string>
        {
            "--model",
            options.ModelPath,
            "--file",
            promptPath,
            "--ctx-size",
            options.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-gpu-layers",
            options.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-predict",
            options.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--temp",
            "0.1",
            "--single-turn",
            "--no-display-prompt"
        };

        if (options.Threads is { } threads)
        {
            arguments.Add("--threads");
            arguments.Add(threads.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (options.ThreadsBatch is { } threadsBatch)
        {
            arguments.Add("--threads-batch");
            arguments.Add(threadsBatch.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return arguments;
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
