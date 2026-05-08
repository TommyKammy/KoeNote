using KoeNote.App.Services.Llm;

namespace KoeNote.App.Tests;

public sealed class LlamaCompletionArgumentBuilderTests
{
    [Fact]
    public void Build_IncludesSharedRuntimeAndTaskArguments()
    {
        var arguments = LlamaCompletionArgumentBuilder.Build(new LlamaCompletionArgumentOptions(
            @"C:\models\model.gguf",
            @"C:\out\prompt.txt",
            ContextSize: 4096,
            GpuLayers: 35,
            MaxTokens: 768,
            Temperature: 0.2,
            Threads: 6,
            ThreadsBatch: 4,
            NoConversation: true,
            TopP: 0.9,
            TopK: 40,
            RepeatPenalty: 1.1,
            JsonSchemaFilePath: @"C:\out\schema.json"));

        Assert.Equal("--model", arguments[0]);
        Assert.Equal(@"C:\models\model.gguf", arguments[1]);
        Assert.Contains("--ctx-size", arguments);
        Assert.Contains("4096", arguments);
        Assert.Contains("--n-gpu-layers", arguments);
        Assert.Contains("35", arguments);
        Assert.Contains("--n-predict", arguments);
        Assert.Contains("768", arguments);
        Assert.Contains("--temp", arguments);
        Assert.Contains("0.2", arguments);
        Assert.Contains("--top-p", arguments);
        Assert.Contains("0.9", arguments);
        Assert.Contains("--top-k", arguments);
        Assert.Contains("40", arguments);
        Assert.Contains("--repeat-penalty", arguments);
        Assert.Contains("1.1", arguments);
        Assert.Contains("--threads", arguments);
        Assert.Contains("6", arguments);
        Assert.Contains("--threads-batch", arguments);
        Assert.Contains("4", arguments);
        Assert.Contains("--no-conversation", arguments);
        Assert.Contains("--no-display-prompt", arguments);
        Assert.Contains("--json-schema-file", arguments);
        Assert.Contains(@"C:\out\schema.json", arguments);
    }

    [Fact]
    public void Build_OmitsOptionalArgumentsWhenUnset()
    {
        var arguments = LlamaCompletionArgumentBuilder.Build(new LlamaCompletionArgumentOptions(
            "model.gguf",
            "prompt.txt",
            ContextSize: 1024,
            GpuLayers: 0,
            MaxTokens: 192,
            Temperature: 0.1,
            NoConversation: false));

        Assert.DoesNotContain("--no-conversation", arguments);
        Assert.DoesNotContain("--top-p", arguments);
        Assert.DoesNotContain("--top-k", arguments);
        Assert.DoesNotContain("--repeat-penalty", arguments);
        Assert.DoesNotContain("--threads", arguments);
        Assert.DoesNotContain("--threads-batch", arguments);
        Assert.DoesNotContain("--json-schema-file", arguments);
        Assert.Contains("--no-display-prompt", arguments);
    }
}
