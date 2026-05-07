using System.IO;
using System.Text;
using KoeNote.App.Services.Review;

namespace KoeNote.App.Services.Transcript;

public sealed class LlamaTranscriptPolishingRuntime(
    ExternalProcessRunner processRunner,
    TranscriptPolishingPromptBuilder promptBuilder)
    : ITranscriptPolishingRuntime
{
    public async Task<TranscriptPolishingChunkResult> PolishChunkAsync(
        TranscriptPolishingOptions options,
        TranscriptPolishingChunk chunk,
        CancellationToken cancellationToken = default)
    {
        ValidateInputs(options, chunk);
        var timeout = options.Timeout ?? TimeSpan.FromHours(2);
        Directory.CreateDirectory(options.OutputDirectory);

        var prompt = promptBuilder.Build(chunk);
        var promptPath = Path.Combine(options.OutputDirectory, $"polish.chunk-{chunk.ChunkIndex:D3}.prompt.txt");
        await File.WriteAllTextAsync(promptPath, prompt, Encoding.UTF8, cancellationToken);

        var start = DateTimeOffset.UtcNow;
        var result = await processRunner.RunAsync(
            options.LlamaCompletionPath,
            BuildArguments(options, promptPath),
            timeout,
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new ReviewWorkerException(
                ReviewFailureCategory.ProcessFailed,
                $"Transcript polishing runtime exited with code {result.ExitCode}: {result.StandardError}");
        }

        var content = NormalizeOutput(result.StandardOutput);
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ReviewWorkerException(ReviewFailureCategory.JsonParseFailed, "Transcript polishing returned empty output.");
        }

        var outputPath = Path.Combine(options.OutputDirectory, $"polish.chunk-{chunk.ChunkIndex:D3}.txt");
        await File.WriteAllTextAsync(outputPath, content, Encoding.UTF8, cancellationToken);
        return new TranscriptPolishingChunkResult(chunk, content, DateTimeOffset.UtcNow - start);
    }

    private static IReadOnlyList<string> BuildArguments(TranscriptPolishingOptions options, string promptPath)
    {
        return
        [
            "--model",
            options.ModelPath,
            "--file",
            promptPath,
            "--ctx-size",
            "8192",
            "--n-gpu-layers",
            "999",
            "--n-predict",
            "4096",
            "--temp",
            "0.1",
            "--single-turn",
            "--no-display-prompt"
        ];
    }

    private static string NormalizeOutput(string output)
    {
        var text = (output ?? string.Empty).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = text.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                text = text[(firstLineEnd + 1)..];
            }

            var fenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceIndex >= 0)
            {
                text = text[..fenceIndex];
            }
        }

        return text.Trim();
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
}
