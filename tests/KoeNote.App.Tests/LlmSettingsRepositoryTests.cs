using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Transcript;

namespace KoeNote.App.Tests;

public sealed class LlmSettingsRepositoryTests
{
    [Fact]
    public void UpsertProfile_PersistsAndReadsActiveProfile()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new LlmSettingsRepository(paths);
        var profile = CreateProfile("builtin:gemma:balanced", "gemma-4-e4b-it-q4-k-m", "gemma");

        repository.UpsertProfile(profile, isActive: true, source: "resolved");

        var persisted = repository.FindProfile(profile.ProfileId);
        Assert.NotNull(persisted);
        Assert.True(persisted.IsActive);
        Assert.Equal("resolved", persisted.Source);
        Assert.Equal(profile, persisted.Profile);
        Assert.Equal(profile, repository.FindActiveProfile()?.Profile);
    }

    [Fact]
    public void UpsertProfile_ActivatingSecondProfileClearsPreviousActiveProfile()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new LlmSettingsRepository(paths);
        var first = CreateProfile("builtin:gemma:balanced", "gemma-4-e4b-it-q4-k-m", "gemma");
        var second = CreateProfile("builtin:bonsai:conservative", "bonsai-8b-q1-0", "bonsai");

        repository.UpsertProfile(first, isActive: true, source: "resolved");
        repository.UpsertProfile(second, isActive: true, source: "resolved");

        Assert.False(repository.FindProfile(first.ProfileId)?.IsActive);
        Assert.True(repository.FindProfile(second.ProfileId)?.IsActive);
        Assert.Equal(second.ProfileId, repository.FindActiveProfile()?.Profile.ProfileId);
    }

    [Fact]
    public void UpsertTaskSettings_PersistsAndUpdatesTaskSettings()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new LlmSettingsRepository(paths);
        var profile = CreateProfile("builtin:bonsai:conservative", "bonsai-8b-q1-0", "bonsai");
        repository.UpsertProfile(profile, isActive: true, source: "resolved");
        var summary = LlmPresetCatalog.ResolveTaskSettings(profile.ModelId, profile.ModelFamily, LlmTaskKind.Summary);
        var updated = summary with { MaxTokens = 640, GenerationProfile = "bonsai-summary-custom" };

        repository.UpsertTaskSettings(profile.ProfileId, summary);
        repository.UpsertTaskSettings(profile.ProfileId, updated);

        var persisted = repository.FindTaskSettings(profile.ProfileId, LlmTaskKind.Summary);
        Assert.NotNull(persisted);
        Assert.Equal($"{profile.ProfileId}:summary", persisted.SettingsId);
        Assert.Equal(profile.ProfileId, persisted.ProfileId);
        Assert.Equal(updated, persisted.Settings);
        Assert.Single(repository.ListTaskSettings(profile.ProfileId));
    }

    [Fact]
    public void ListProfiles_ReturnsActiveProfileFirst()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new LlmSettingsRepository(paths);
        var first = CreateProfile("builtin:gemma:balanced", "gemma-4-e4b-it-q4-k-m", "gemma");
        var second = CreateProfile("builtin:bonsai:conservative", "bonsai-8b-q1-0", "bonsai");

        repository.UpsertProfile(first, isActive: false, source: "resolved");
        repository.UpsertProfile(second, isActive: true, source: "resolved");

        var profiles = repository.ListProfiles();

        Assert.Equal([second.ProfileId, first.ProfileId], profiles.Select(profile => profile.Profile.ProfileId).ToArray());
    }

    private static LlmRuntimeProfile CreateProfile(string profileId, string modelId, string family)
    {
        return new LlmRuntimeProfile(
            profileId,
            modelId,
            family,
            modelId,
            "llama-cpp",
            "runtime-llama-cpp",
            "model.gguf",
            "llama-completion.exe",
            8192,
            999,
            null,
            null,
            true,
            LlmOutputSanitizerProfiles.None,
            TimeSpan.FromHours(2));
    }
}
