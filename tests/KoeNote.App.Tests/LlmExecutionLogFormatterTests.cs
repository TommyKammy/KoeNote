using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlmExecutionLogFormatterTests
{
    [Fact]
    public void Format_IncludesProfileTaskRuntimeAndGenerationSettings()
    {
        var profile = new LlmRuntimeProfile(
            "builtin:bonsai-8b-q1-0:bonsai:conservative",
            "bonsai-8b-q1-0",
            "bonsai",
            "Bonsai 8B Q1_0",
            "llama-cpp",
            "runtime-llama-cpp",
            "model.gguf",
            "llama-completion.exe",
            8192,
            999,
            null,
            null,
            true,
            LlmOutputSanitizerProfiles.Strict,
            TimeSpan.FromHours(2))
        {
            AccelerationMode = "cuda"
        };
        var settings = LlmPresetCatalog.ResolveTaskSettings("bonsai-8b-q1-0", "bonsai", LlmTaskKind.Summary);

        var log = LlmExecutionLogFormatter.Format(profile, settings);

        Assert.Contains("task=summary", log, StringComparison.Ordinal);
        Assert.Contains("profile=builtin:bonsai-8b-q1-0:bonsai:conservative", log, StringComparison.Ordinal);
        Assert.Contains("model=bonsai-8b-q1-0", log, StringComparison.Ordinal);
        Assert.Contains("family=bonsai", log, StringComparison.Ordinal);
        Assert.Contains("runtime=llama-cpp/runtime-llama-cpp", log, StringComparison.Ordinal);
        Assert.Contains("acceleration=cuda", log, StringComparison.Ordinal);
        Assert.Contains("runtime_path=\"llama-completion.exe\"", log, StringComparison.Ordinal);
        Assert.Contains("ctx=8192", log, StringComparison.Ordinal);
        Assert.Contains("gpu_layers=999", log, StringComparison.Ordinal);
        Assert.Contains("threads=-", log, StringComparison.Ordinal);
        Assert.Contains("timeout_sec=7200", log, StringComparison.Ordinal);
        Assert.Contains("generation=bonsai-summary-conservative", log, StringComparison.Ordinal);
        Assert.Contains("temp=0.1", log, StringComparison.Ordinal);
        Assert.Contains("max_tokens=512", log, StringComparison.Ordinal);
        Assert.Contains("chunk_segments=40", log, StringComparison.Ordinal);
        Assert.Contains("json_schema=false", log, StringComparison.Ordinal);
        Assert.Contains("repair=false", log, StringComparison.Ordinal);
        Assert.Contains("sanitizer=strict", log, StringComparison.Ordinal);
    }
}
