using KoeNote.App.Services;
using KoeNote.App.Services.Llm;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Tests;

public sealed class LlmSettingsSeedServiceTests
{
    [Fact]
    public void EnsureActiveProfileFromSetup_SeedsSelectedReviewModelAndTaskSettings()
    {
        var paths = TestDatabase.CreateReadyPaths();
        new SetupStateService(paths).Save(SetupState.Default(paths.DefaultModelStorageRoot) with
        {
            SelectedReviewModelId = "bonsai-8b-q1-0",
            SelectedModelPresetId = "lightweight"
        });
        var repository = new LlmSettingsRepository(paths);
        var service = CreateService(paths, repository);

        var seeded = service.EnsureActiveProfileFromSetup();

        Assert.True(seeded);
        var active = repository.FindActiveProfile();
        Assert.NotNull(active);
        Assert.Equal("bonsai-8b-q1-0", active.Profile.ModelId);
        Assert.Equal("setup-state", active.Source);
        Assert.Equal("builtin:bonsai-8b-q1-0:bonsai:conservative", active.Profile.ProfileId);
        var taskSettings = repository.ListTaskSettings(active.Profile.ProfileId);
        Assert.Equal(3, taskSettings.Count);
        Assert.Contains(taskSettings, item =>
            item.Settings.TaskKind == LlmTaskKind.Summary &&
            item.Settings.GenerationProfile == "bonsai-summary-conservative");
    }

    [Fact]
    public void EnsureActiveProfileFromSetup_DoesNotOverwriteExistingActiveProfile()
    {
        var paths = TestDatabase.CreateReadyPaths();
        var repository = new LlmSettingsRepository(paths);
        var existing = new LlmRuntimeProfile(
            "custom:active",
            "custom-model",
            null,
            "Custom Model",
            "llama-cpp",
            "runtime-llama-cpp",
            "model.gguf",
            "llama-completion.exe",
            4096,
            0,
            4,
            4,
            true,
            "none",
            TimeSpan.FromMinutes(30));
        repository.UpsertProfile(existing, isActive: true, source: "user");
        var service = CreateService(paths, repository);

        var seeded = service.EnsureActiveProfileFromSetup();

        Assert.False(seeded);
        Assert.Equal(existing.ProfileId, repository.FindActiveProfile()?.Profile.ProfileId);
        Assert.Empty(repository.ListTaskSettings(existing.ProfileId));
    }

    [Fact]
    public void EnsureActiveProfileFromSetup_CanOverwriteExistingActiveProfileForSetupSync()
    {
        var paths = TestDatabase.CreateReadyPaths();
        new SetupStateService(paths).Save(SetupState.Default(paths.DefaultModelStorageRoot) with
        {
            SelectedReviewModelId = "gemma-4-e4b-it-q4-k-m"
        });
        var repository = new LlmSettingsRepository(paths);
        repository.UpsertProfile(
            new LlmRuntimeProfile(
                "custom:active",
                "custom-model",
                null,
                "Custom Model",
                "llama-cpp",
                "runtime-llama-cpp",
                "model.gguf",
                "llama-completion.exe",
                4096,
                0,
                4,
                4,
                true,
                "none",
                TimeSpan.FromMinutes(30)),
            isActive: true,
            source: "user");
        var service = CreateService(paths, repository);

        var seeded = service.EnsureActiveProfileFromSetup(overwriteActive: true);

        Assert.True(seeded);
        var active = repository.FindActiveProfile();
        Assert.NotNull(active);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", active.Profile.ModelId);
        Assert.Equal(3, repository.ListTaskSettings(active.Profile.ProfileId).Count);
    }

    [Fact]
    public void EnsureActiveProfileFromSetup_FallsBackToPresetWhenSelectedReviewModelIsUnsupported()
    {
        var paths = TestDatabase.CreateReadyPaths();
        new SetupStateService(paths).Save(SetupState.Default(paths.DefaultModelStorageRoot) with
        {
            SelectedModelPresetId = "recommended",
            SelectedReviewModelId = "unsupported-model"
        });
        var repository = new LlmSettingsRepository(paths);
        var service = CreateService(paths, repository);

        service.EnsureActiveProfileFromSetup();

        Assert.Equal("gemma-4-e4b-it-q4-k-m", repository.FindActiveProfile()?.Profile.ModelId);
    }

    private static LlmSettingsSeedService CreateService(AppPaths paths, LlmSettingsRepository repository)
    {
        return new LlmSettingsSeedService(
            paths,
            new ModelCatalogService(paths),
            new InstalledModelRepository(paths),
            new SetupStateService(paths),
            repository);
    }
}
