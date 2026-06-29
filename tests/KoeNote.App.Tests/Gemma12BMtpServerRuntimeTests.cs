using System.Text.Json;
using KoeNote.App.Services;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

[Collection(Gemma12BEnvironmentCollection.Name)]
public sealed class Gemma12BMtpServerRuntimeTests
{
    [Fact]
    public void BuildServerEndpoint_DoesNotCreateDoubleSlashPaths()
    {
        var baseUri = new Uri("http://127.0.0.1:49174");

        Assert.Equal(
            "http://127.0.0.1:49174/health",
            LlamaTranscriptPolishingRuntime.BuildServerEndpoint(baseUri, "health").ToString());
        Assert.Equal(
            "http://127.0.0.1:49174/v1/chat/completions",
            LlamaTranscriptPolishingRuntime.BuildServerEndpoint(baseUri, "v1/chat/completions").ToString());
    }

    [Fact]
    public void BuildServerChatCompletionRequestJson_OmitsNullReasoningContent()
    {
        var options = new TranscriptPolishingOptions(
            "job-001",
            "llama-completion.exe",
            "model.gguf",
            "output",
            Gemma12BLocalValidation.ModelId,
            MaxTokens: 123,
            Temperature: 0,
            RepeatPenalty: 1.08);

        var json = LlamaTranscriptPolishingRuntime.BuildServerChatCompletionRequestJson(options, "prompt");

        Assert.DoesNotContain("reasoning_content", json, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(Gemma12BLocalValidation.ModelId, root.GetProperty("model").GetString());
        Assert.Equal(123, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(1.08, root.GetProperty("repeat_penalty").GetDouble());
        Assert.False(root.GetProperty("stream").GetBoolean());
        var message = root.GetProperty("messages")[0];
        Assert.Equal("user", message.GetProperty("role").GetString());
        Assert.Equal("prompt", message.GetProperty("content").GetString());
    }

    [Fact]
    public void IsMtpServerEnabled_DefaultsOnForDistributionAndCanBeDisabled()
    {
        var previousValidation = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.EnableEnvironmentVariable);
        var previousMtp = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableEnvironmentVariable, null);
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, null);
            Assert.True(Gemma12BLocalValidation.IsMtpServerEnabled());

            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableEnvironmentVariable, "1");
            Assert.True(Gemma12BLocalValidation.IsMtpServerEnabled());

            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, "0");
            Assert.False(Gemma12BLocalValidation.IsMtpServerEnabled());
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableEnvironmentVariable, previousValidation);
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.EnableMtpServerEnvironmentVariable, previousMtp);
        }
    }

    [Fact]
    public void ResolveLlamaServerPath_UsesEnvironmentOverrideOrCompletionSibling()
    {
        var previous = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.LlamaServerPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.LlamaServerPathEnvironmentVariable, null);
            Assert.Equal(
                Path.Combine("C:\\runtime", "llama-server.exe"),
                Gemma12BLocalValidation.ResolveLlamaServerPath(Path.Combine("C:\\runtime", "llama-completion.exe")));

            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.LlamaServerPathEnvironmentVariable, "C:\\custom\\llama-server.exe");
            Assert.Equal(
                "C:\\custom\\llama-server.exe",
                Gemma12BLocalValidation.ResolveLlamaServerPath(Path.Combine("C:\\runtime", "llama-completion.exe")));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.LlamaServerPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void ResolveMtpDraftModelPath_UsesReviewAuxDefaultFolder()
    {
        var previous = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, null);

            var path = Gemma12BLocalValidation.ResolveMtpDraftModelPath();

            Assert.Contains(
                Path.Combine("models", "review_aux", Gemma12BLocalValidation.MtpDraftModelId),
                path,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public void LlamaServerHelpSupportsMtp_RequiresDraftMtpOptions()
    {
        Assert.True(Gemma12BLocalValidation.LlamaServerHelpSupportsMtp("""
            --spec-type TYPE
            draft-mtp
            --model-draft FNAME
            """));
        Assert.False(Gemma12BLocalValidation.LlamaServerHelpSupportsMtp("""
            --model FNAME
            --ctx-size N
            """));
    }

    [Fact]
    public void ResolveMtpDraftModelPath_SkipsUnbridgeableInstalledDraft()
    {
        var previous = Environment.GetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, null);
            var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
            var paths = new AppPaths(root, root, AppContext.BaseDirectory);
            paths.EnsureCreated();
            new DatabaseInitializer(paths).EnsureCreated();
            var installedModels = new InstalledModelRepository(paths);
            var badDraftPath = Path.Combine(paths.UserModels, "review_aux", Gemma12BLocalValidation.MtpDraftModelId, "directory-draft.gguf");
            Directory.CreateDirectory(badDraftPath);
            installedModels.UpsertInstalledModel(new InstalledModel(
                Gemma12BLocalValidation.MtpDraftModelId,
                "review_aux",
                "llama-cpp",
                Gemma12BLocalValidation.MtpDraftModelId,
                Family: null,
                Version: null,
                badDraftPath,
                ManifestPath: null,
                SizeBytes: 0,
                Sha256: null,
                Verified: true,
                LicenseName: "test",
                SourceType: "test",
                InstalledAt: DateTimeOffset.UtcNow,
                LastVerifiedAt: DateTimeOffset.UtcNow,
                Status: "installed"));
            var fallbackDraftPath = Gemma12BLocalValidation.ResolveMtpDraftModelPath(paths.UserModels);
            Directory.CreateDirectory(Path.GetDirectoryName(fallbackDraftPath)!);
            File.WriteAllText(fallbackDraftPath, "draft");
            new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
            {
                StorageRoot = paths.UserModels
            });
            var runner = new ReadablePolishingStageRunner(
                paths,
                new JobRepository(paths),
                new StageProgressRepository(paths),
                new JobLogRepository(paths),
                installedModels,
                new SetupStateService(paths),
                new TranscriptPolishingService(
                    new TranscriptReadRepository(paths),
                    new TranscriptDerivativeRepository(paths),
                    new LlamaTranscriptPolishingRuntime(new ExternalProcessRunner(), new TranscriptPolishingPromptBuilder())),
                new ReadablePolishingPromptSettingsRepository(paths));

            Assert.Equal(fallbackDraftPath, runner.ResolveMtpDraftModelPath());
        }
        finally
        {
            Environment.SetEnvironmentVariable(Gemma12BLocalValidation.DraftModelPathEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task PolishChunkAsync_FailsFastWhenMtpServerRuntimeIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var llamaCompletionPath = Path.Combine(root, "runtime", "llama-completion.exe");
        var modelPath = Path.Combine(root, "models", "model.gguf");
        var draftPath = Path.Combine(root, "models", "draft.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(llamaCompletionPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(llamaCompletionPath, "runtime");
        File.WriteAllText(modelPath, "model");
        File.WriteAllText(draftPath, "draft");
        var runtime = new LlamaTranscriptPolishingRuntime(new ExternalProcessRunner(), new TranscriptPolishingPromptBuilder());
        var options = new TranscriptPolishingOptions(
            "job-001",
            llamaCompletionPath,
            modelPath,
            Path.Combine(root, "output"),
            Gemma12BLocalValidation.ModelId,
            UseLlamaServerChatMtp: true,
            LlamaServerPath: Path.Combine(root, "missing", "llama-server.exe"),
            MtpDraftModelPath: draftPath);

        var exception = await Assert.ThrowsAsync<ReviewWorkerException>(() =>
            runtime.PolishChunkAsync(
                options,
                new TranscriptPolishingChunk(1, [
                    new TranscriptReadModel("000001", 0, 1, "Speaker_0", "hello", "", "Speaker_0", "hello", null, null)
                ])));

        Assert.Equal(ReviewFailureCategory.MissingRuntime, exception.Category);
        Assert.Contains("llama-server runtime not found", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
