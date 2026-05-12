using System.Text.Json;
using System.IO.Compression;
using System.Net;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Diarization;
using KoeNote.App.Services.Models;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Setup;

namespace KoeNote.App.Tests;

public sealed class SetupWizardServiceTests
{
    [Fact]
    public void SetupStateService_DefaultsToIncompleteAndPersists()
    {
        var paths = CreatePaths();
        var service = new SetupStateService(paths);

        var initial = service.Load();
        var saved = service.Save(initial with
        {
            SetupMode = "offline_pack",
            LicenseAccepted = true,
            SelectedAsrModelId = "faster-whisper-large-v3-turbo"
        });

        var reloaded = service.Load();

        Assert.False(initial.IsCompleted);
        Assert.Equal("offline_pack", reloaded.SetupMode);
        Assert.True(reloaded.LicenseAccepted);
        Assert.Equal(saved.SelectedAsrModelId, reloaded.SelectedAsrModelId);
    }

    [Fact]
    public void SetupStep_ValuesPreservePersistedStateCompatibility()
    {
        Assert.Equal(0, (int)SetupStep.Welcome);
        Assert.Equal(6, (int)SetupStep.License);
        Assert.Equal(7, (int)SetupStep.Install);
        Assert.Equal(8, (int)SetupStep.SmokeTest);
        Assert.Equal(9, (int)SetupStep.Complete);
        Assert.Equal(10, (int)SetupStep.InstallPlan);
    }

    [Fact]
    public void SetupStateService_AllUsersScope_DefaultsStorageToMachineModels()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var service = new SetupStateService(paths);

        var initial = service.Load();

        Assert.Equal(paths.MachineModels, initial.StorageRoot);
    }

    [Fact]
    public void SetupWizard_ListsSelectableAsrModelsAndReviewChoices()
    {
        var wizard = CreateWizard(CreatePaths());

        var asrModels = wizard.GetSelectableModels("asr");
        var reviewModels = wizard.GetSelectableModels("review");

        Assert.Equal(
            ["faster-whisper-large-v3", "faster-whisper-large-v3-turbo", "kotoba-whisper-v2.2-faster", "whisper-base", "whisper-small"],
            asrModels.Select(entry => entry.ModelId).Order(StringComparer.OrdinalIgnoreCase).ToArray());
        Assert.NotEmpty(reviewModels);
        Assert.DoesNotContain(reviewModels, entry => entry.ModelId == "ternary-bonsai-8b-q2-0");
    }

    [Fact]
    public void SetupWizard_RecommendedSelections_UseInstallScopeDefaultStorage()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var wizard = CreateWizard(paths);

        var state = wizard.UseRecommendedSelections();

        Assert.Equal(paths.MachineModels, state.StorageRoot);
        Assert.Equal("faster-whisper-large-v3-turbo", state.SelectedAsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", state.SelectedReviewModelId);
        Assert.Equal("recommended", state.SelectedModelPresetId);
        Assert.Equal("recommended", state.SetupMode);
        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.True(state.LicenseAccepted);
    }

    [Fact]
    public void SetupWizard_MoveNext_DoesNotEnterInstallBeforeLicenseAccepted()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.License,
            LicenseAccepted = false
        });

        var state = wizard.MoveNext();

        Assert.Equal(SetupStep.License, state.CurrentStep);
        Assert.False(state.LicenseAccepted);
    }

    [Fact]
    public void SetupWizard_AcceptLicenses_MovesToInstallPlan()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);

        var state = wizard.AcceptLicenses();

        Assert.True(state.LicenseAccepted);
        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
    }

    [Fact]
    public void SetupWizard_Navigation_FollowsLicenseInstallPlanInstallOrder()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);

        var state = wizard.AcceptLicenses();
        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);

        state = wizard.MoveNext();
        Assert.Equal(SetupStep.Install, state.CurrentStep);

        state = wizard.MoveBack();
        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);

        state = wizard.MoveBack();
        Assert.Equal(SetupStep.SetupMode, state.CurrentStep);
    }

    [Fact]
    public void SetupWizard_GuidedNext_ReachesInstallPlanAndAcceptsLicenses()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 16L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: 6,
            nvidiaGpuDetected: true,
            logicalProcessorCount: 8));

        var state = wizard.MoveNextGuided();
        Assert.Equal(SetupStep.SetupMode, state.CurrentStep);

        state = wizard.MoveNextGuided();
        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.Equal("recommended", state.SelectedModelPresetId);
        Assert.Equal("faster-whisper-large-v3-turbo", state.SelectedAsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", state.SelectedReviewModelId);
        Assert.True(state.LicenseAccepted);
    }

    [Fact]
    public void SetupWizard_GuidedNext_AppliesHostRecommendedPresetWhenDefaultRecommendedIsSelected()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 8L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: false,
            logicalProcessorCount: 4));

        wizard.MoveNextGuided();
        var state = wizard.MoveNextGuided();

        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.Equal("ultra_lightweight", state.SelectedModelPresetId);
        Assert.Equal("whisper-base", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_GuidedNext_DoesNotDownrankNvidiaGpuWhenVramIsUnknown()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 16L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: true,
            logicalProcessorCount: 8));

        wizard.MoveNextGuided();
        var state = wizard.MoveNextGuided();

        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.Equal("recommended", state.SelectedModelPresetId);
        Assert.Equal("faster-whisper-large-v3-turbo", state.SelectedAsrModelId);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_GuidedNext_DoesNotOverrideCustomModelSelection()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        new SetupStateService(paths).Save(wizard.LoadState() with { CurrentStep = SetupStep.SetupMode });

        var state = wizard.MoveNextGuided();

        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.Equal("custom", state.SetupMode);
        Assert.Null(state.SelectedModelPresetId);
        Assert.Equal("kotoba-whisper-v2.2-faster", state.SelectedAsrModelId);
        Assert.True(state.LicenseAccepted);
    }

    [Fact]
    public async Task SetupWizard_GuidedNext_StopsAtInstallPlanWithoutStartingInstallation()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, new HttpClient(new FailingHandler()));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.InstallPlan,
            LicenseAccepted = true
        });

        var state = wizard.MoveNextGuided();

        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.True(state.LicenseAccepted);

        var installResult = await wizard.InstallSelectedPresetModelsAsync(progress: null);
        Assert.False(installResult.IsSucceeded);
        Assert.Equal(SetupStep.Install, wizard.LoadState().CurrentStep);
    }

    [Fact]
    public void SetupWizard_GuidedNext_RepairsInvalidInstallPlanWithoutAcceptance()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.InstallPlan,
            LicenseAccepted = false
        });

        var state = wizard.MoveNextGuided();

        Assert.Equal(SetupStep.InstallPlan, state.CurrentStep);
        Assert.True(state.LicenseAccepted);
    }

    [Fact]
    public async Task SetupWizard_InstallSelectedPresetModels_RequiresInstallPlan()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        wizard.SelectModelPreset("ultra_lightweight");
        new SetupStateService(paths).Save(wizard.LoadState() with
        {
            LicenseAccepted = true,
            CurrentStep = SetupStep.SetupMode
        });

        var result = await wizard.InstallSelectedPresetModelsAsync(progress: null);

        Assert.False(result.IsSucceeded);
        Assert.Contains("installation plan", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SetupStep.SetupMode, wizard.LoadState().CurrentStep);
    }

    [Fact]
    public async Task SetupWizard_InstallSelectedPresetModels_RequiresLicenseAccepted()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        wizard.SelectModelPreset("ultra_lightweight");

        var result = await wizard.InstallSelectedPresetModelsAsync(progress: null);

        Assert.False(result.IsSucceeded);
        Assert.Contains("licenses", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SetupStep.SetupMode, wizard.LoadState().CurrentStep);
    }

    [Fact]
    public void SetupWizard_SelectModelPreset_AppliesAsrAndReviewModels()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        var wizard = CreateWizard(paths);

        var presets = wizard.GetModelPresets();
        var lightweight = Assert.Single(presets, preset => preset.PresetId == "lightweight");
        var state = wizard.SelectModelPreset(lightweight.PresetId);

        Assert.Equal("lightweight", state.SelectedModelPresetId);
        Assert.Equal("lightweight", state.SetupMode);
        Assert.Equal("whisper-small", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_SelectModel_InvalidatesCompletedSmokeState()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            CurrentStep = SetupStep.Complete,
            SelectedReviewModelId = "llm-jp-4-8b-thinking-q4-k-m"
        });
        var wizard = CreateWizard(paths);

        var state = wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");

        Assert.False(state.IsCompleted);
        Assert.False(state.LastSmokeSucceeded);
        Assert.Equal(SetupStep.ReviewModel, state.CurrentStep);
        Assert.Equal("llm-jp-4-8b-thinking-q4-k-m", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_SelectModel_RejectsHiddenTernaryReviewModel()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        var wizard = CreateWizard(paths);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            wizard.SelectModel("review", "ternary-bonsai-8b-q2-0"));

        Assert.Contains("not selectable", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupWizard_LoadState_RepairsHiddenTernaryReviewSelection()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            LastSmokeSucceeded = true,
            CurrentStep = SetupStep.Complete,
            SetupMode = "custom",
            SelectedModelPresetId = null,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "ternary-bonsai-8b-q2-0"
        });
        var wizard = CreateWizard(paths);

        var state = wizard.LoadState();

        Assert.False(state.IsCompleted);
        Assert.False(state.LastSmokeSucceeded);
        Assert.Equal(SetupStep.ReviewModel, state.CurrentStep);
        Assert.Equal("gemma-4-e4b-it-q4-k-m", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_LoadState_PreservesSupportedBonsaiLightweightReviewModel()
    {
        var paths = CreatePaths();
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            IsCompleted = true,
            SetupMode = "lightweight",
            SelectedModelPresetId = "lightweight",
            SelectedAsrModelId = "whisper-small",
            SelectedReviewModelId = "bonsai-8b-q1-0"
        });
        var wizard = CreateWizard(paths);

        var state = wizard.LoadState();

        Assert.Equal("lightweight", state.SelectedModelPresetId);
        Assert.Equal("whisper-small", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_SelectModelPreset_PreservesChosenStorageRoot()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        var customRoot = Path.Combine(paths.Root, "custom-model-storage");

        wizard.SetStorageRoot(customRoot);
        var state = wizard.SelectModelPreset("lightweight");

        Assert.Equal(customRoot, state.StorageRoot);
        Assert.Equal("lightweight", state.SelectedModelPresetId);
    }

    [Fact]
    public void SetupWizard_CommitSelectionDraft_PersistsDraftWithoutLosingStorageRoot()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        var customRoot = Path.Combine(paths.Root, "custom-model-storage");
        wizard.SetStorageRoot(customRoot);
        var draft = wizard.LoadState() with
        {
            SetupMode = "custom",
            SelectedModelPresetId = null,
            SelectedAsrModelId = "whisper-small",
            SelectedReviewModelId = "bonsai-8b-q1-0",
            LicenseAccepted = true,
            LastSmokeSucceeded = true
        };

        var state = wizard.CommitSelectionDraft(draft);

        Assert.Equal("custom", state.SetupMode);
        Assert.Null(state.SelectedModelPresetId);
        Assert.Equal("whisper-small", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
        Assert.Equal(customRoot, state.StorageRoot);
        Assert.False(state.IsCompleted);
        Assert.False(state.LastSmokeSucceeded);
    }

    [Fact]
    public async Task SetupWizard_InstallSelectedPresetModels_SkipsAlreadyInstalledAsrAndReviewModels()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Directory.CreateDirectory(paths.WhisperBaseModelPath);
        var reviewPath = Path.Combine(paths.UserModels, "review", "bonsai-8b-q1-0", "Bonsai-8B-Q1_0.gguf");
        Touch(reviewPath);
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "whisper-base", "asr", "whisper-base", paths.WhisperBaseModelPath);
        UpsertVerified(installedModels, "bonsai-8b-q1-0", "review", "llama-cpp", reviewPath);
        var wizard = CreateWizard(paths);
        wizard.SelectModelPreset("ultra_lightweight");
        wizard.AcceptLicenses();

        var result = await wizard.InstallSelectedPresetModelsAsync(progress: null);

        Assert.True(result.IsSucceeded);
        Assert.Equal(2, result.InstalledModels.Count);
        Assert.Contains(result.InstalledModels, model => model.ModelId == "whisper-base");
        Assert.Contains(result.InstalledModels, model => model.ModelId == "bonsai-8b-q1-0");
        Assert.Equal(SetupStep.Install, wizard.LoadState().CurrentStep);
    }

    [Fact]
    public void SetupWizard_AutomaticPresetRecommendation_SelectsLightweightForLowResourceHost()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 8L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: false,
            logicalProcessorCount: 4));

        var recommendation = wizard.GetPresetRecommendation();
        var state = wizard.ApplyAutomaticModelPresetRecommendation();

        Assert.Equal("ultra_lightweight", recommendation.PresetId);
        Assert.Equal(SetupStep.Welcome, state.CurrentStep);
        Assert.Equal("ultra_lightweight", state.SelectedModelPresetId);
        Assert.Equal("whisper-base", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_AutomaticPresetRecommendation_SelectsLightweightForCapableCpuOnlyHost()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 16L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: false,
            logicalProcessorCount: 8));

        var recommendation = wizard.GetPresetRecommendation();
        var state = wizard.ApplyAutomaticModelPresetRecommendation();

        Assert.Equal("lightweight", recommendation.PresetId);
        Assert.Contains("CPU/RAM", recommendation.Detail, StringComparison.Ordinal);
        Assert.Equal(SetupStep.Welcome, state.CurrentStep);
        Assert.Equal("lightweight", state.SelectedModelPresetId);
        Assert.Equal("whisper-small", state.SelectedAsrModelId);
        Assert.Equal("bonsai-8b-q1-0", state.SelectedReviewModelId);
    }

    [Fact]
    public void SetupWizard_AutomaticPresetRecommendation_UsesRamFallbackWhenCpuCountIsUnknown()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 16L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: false,
            logicalProcessorCount: null));

        var recommendation = wizard.GetPresetRecommendation();

        Assert.Equal("lightweight", recommendation.PresetId);
    }

    [Fact]
    public void SetupWizard_AutomaticPresetRecommendation_DoesNotOverrideManualModelChoice()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 8L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: false,
            logicalProcessorCount: 4));

        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        var state = wizard.ApplyAutomaticModelPresetRecommendation();

        Assert.Null(state.SelectedModelPresetId);
        Assert.Equal("custom", state.SetupMode);
        Assert.Equal("kotoba-whisper-v2.2-faster", state.SelectedAsrModelId);
    }

    [Fact]
    public void SetupWizard_AutomaticPresetRecommendation_RunsOnlyOnce()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 8L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: null,
            nvidiaGpuDetected: false,
            logicalProcessorCount: 4));

        wizard.ApplyAutomaticModelPresetRecommendation();
        wizard.SelectModelPreset("recommended");
        wizard.MoveBack();
        wizard.MoveBack();
        var state = wizard.MoveBack();
        state = wizard.ApplyAutomaticModelPresetRecommendation();

        Assert.Equal(SetupStep.Welcome, state.CurrentStep);
        Assert.Equal("recommended", state.SelectedModelPresetId);
        Assert.Equal("faster-whisper-large-v3-turbo", state.SelectedAsrModelId);
    }

    [Fact]
    public void SetupWizard_BlankStorageRoot_UsesInstallScopeDefaultStorage()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var wizard = CreateWizard(paths);

        var state = wizard.SetStorageRoot(" ");

        Assert.Equal(paths.MachineModels, state.StorageRoot);
        Assert.True(Directory.Exists(paths.MachineModels));
    }

    [Fact]
    public void SetupWizard_SmokeCheckWritesFailureReportWithoutBreakingState()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        wizard.UseRecommendedSelections();
        wizard.AcceptLicenses();

        var result = wizard.RunSmokeCheck();

        Assert.False(result.IsSucceeded);
        Assert.True(File.Exists(paths.SetupReportPath));
        Assert.False(wizard.LoadState().IsCompleted);

        using var document = JsonDocument.Parse(File.ReadAllText(paths.SetupReportPath));
        Assert.False(document.RootElement.GetProperty("is_complete").GetBoolean());
    }

    [Fact]
    public void SetupWizard_ExistingDataSummary_DetectsReinstallReusableData()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        Directory.CreateDirectory(Path.Combine(paths.Jobs, "job-1"));
        Directory.CreateDirectory(Path.Combine(paths.UserModels, "asr", "model-1"));
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels));
        var wizard = CreateWizard(paths);

        var summary = wizard.GetExistingDataSummary();

        Assert.Contains(summary, item => item.Name == "setup state" && item.Exists);
        Assert.Contains(summary, item => item.Name == "jobs database" && item.Exists);
        Assert.Contains(summary, item => item.Name == "job folders" && item.Exists);
        Assert.Contains(summary, item => item.Name == "user model storage" && item.Exists);
    }

    [Fact]
    public void SetupWizard_CompleteIfReady_RequiresGpuRuntimesWhenNvidiaGpuIsDetected()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "kotoba-whisper-v2.2-faster", "asr", "kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 32L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: 24,
            nvidiaGpuDetected: true));
        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();

        var smoke = wizard.RunSmokeCheck();
        var completed = wizard.CompleteIfReady();

        Assert.False(smoke.IsSucceeded);
        Assert.False(completed.IsCompleted);
        Assert.Equal(SetupStep.SmokeTest, completed.CurrentStep);
        Assert.Contains(smoke.Checks, check => check.Name == "ASR CUDA runtime" && !check.IsOk);
        Assert.Contains(smoke.Checks, check => check.Name == "Review CUDA runtime" && !check.IsOk);
        Assert.Contains(smoke.Checks, check => check.Name == "speaker diarization runtime" && !check.IsOk);
    }

    [Fact]
    public void SetupWizard_CompletesAfterVerifiedModelsSmokeAndGpuRuntimesPass()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        CreateDiarizationRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "cudart64_12.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "cudnn64_9.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.exe"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "crispasr.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "whisper.dll"));
        Touch(Path.Combine(paths.AsrRuntimeDirectory, "ggml-cuda.dll"));
        Touch(paths.AsrCudaRuntimeMarkerPath);
        CreateCudaReviewRuntime(paths);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "kotoba-whisper-v2.2-faster", "asr", "kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths, hostResourceProbe: new FixedHostResourceProbe(
            totalMemoryBytes: 32L * 1024 * 1024 * 1024,
            maxGpuMemoryGb: 24,
            nvidiaGpuDetected: true));
        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();

        var smoke = wizard.RunSmokeCheck();
        var completed = wizard.CompleteIfReady();

        Assert.True(smoke.IsSucceeded);
        Assert.Contains(smoke.Checks, check => check.Name == "Review CUDA runtime" && check.IsOk);
        Assert.Contains(smoke.Checks, check => check.Name == "ASR runtime smoke" && check.IsOk);
        Assert.Contains(smoke.Checks, check => check.Name == "Review runtime smoke" && check.IsOk);
        Assert.Contains(smoke.Checks, check => check.Name == "speaker diarization smoke" && check.IsOk);
        Assert.True(completed.IsCompleted);
        Assert.Equal(SetupStep.Complete, completed.CurrentStep);

        var rerunSmoke = wizard.RunSmokeCheck();
        var rerunState = wizard.LoadState();

        Assert.True(rerunSmoke.IsSucceeded);
        Assert.True(rerunState.IsCompleted);
        Assert.Equal(SetupStep.Complete, rerunState.CurrentStep);
    }

    [Fact]
    public void SetupWizard_CompleteIfReady_RejectsDiarizationRuntimeWhenPackageDataIsMissing()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize-0.1.2.dist-info"));
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "kotoba-whisper-v2.2-faster", "asr", "kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths);
        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();

        var smoke = wizard.RunSmokeCheck();
        var completed = wizard.CompleteIfReady();

        Assert.False(smoke.IsSucceeded);
        Assert.False(completed.IsCompleted);
        var check = Assert.Single(smoke.Checks, check => check.Name == "speaker diarization runtime");
        Assert.False(check.IsOk);
        Assert.Contains("silero_vad.jit", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupWizard_CompleteIfReady_RejectsMissingAsrWorkerScriptInRuntimeSmoke()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.AsrPythonPath);
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper"));
        CreateDiarizationRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "kotoba-whisper-v2.2-faster", "asr", "kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths);
        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();

        var smoke = wizard.RunSmokeCheck();
        var completed = wizard.CompleteIfReady();

        Assert.False(smoke.IsSucceeded);
        Assert.False(completed.IsCompleted);
        var check = Assert.Single(smoke.Checks, check => check.Name == "ASR runtime smoke");
        Assert.False(check.IsOk);
        Assert.Contains("ASR worker script is missing", check.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupWizard_CompleteIfReady_RequiresTernaryRuntimeForTernaryReviewModel()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        CreateDiarizationRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        var ternaryModelPath = Path.Combine(paths.UserModels, "review", "ternary-bonsai-8b-q2-0", "Ternary-Bonsai-8B-Q2_0.gguf");
        Touch(ternaryModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "kotoba-whisper-v2.2-faster", "asr", "kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath);
        UpsertVerified(installedModels, "ternary-bonsai-8b-q2-0", "review", "ternary-bonsai", ternaryModelPath);
        new SetupStateService(paths).Save(SetupState.Default(paths.UserModels) with
        {
            CurrentStep = SetupStep.SmokeTest,
            LicenseAccepted = true,
            SelectedAsrModelId = "kotoba-whisper-v2.2-faster",
            SelectedReviewModelId = "ternary-bonsai-8b-q2-0"
        });
        var wizard = CreateWizard(paths);

        var smoke = wizard.RunSmokeCheck();
        var completed = wizard.CompleteIfReady();

        Assert.False(smoke.IsSucceeded);
        Assert.False(completed.IsCompleted);
        Assert.Equal(SetupStep.SmokeTest, completed.CurrentStep);
        var runtimeCheck = Assert.Single(smoke.Checks, check => check.Name == "Review LLM runtime");
        Assert.False(runtimeCheck.IsOk);
        Assert.Contains(paths.TernaryLlamaCompletionPath, runtimeCheck.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupWizard_CompleteIfReady_RechecksCurrentModelFiles()
    {
        var paths = CreatePathsWithoutTernaryRuntime();
        Touch(paths.FfmpegPath);
        Touch(paths.LlamaCompletionPath);
        CreateFasterWhisperRuntime(paths);
        CreateDiarizationRuntime(paths);
        Directory.CreateDirectory(paths.KotobaWhisperFasterModelPath);
        Touch(paths.ReviewModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "kotoba-whisper-v2.2-faster", "asr", "kotoba-whisper-v2.2-faster", paths.KotobaWhisperFasterModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths);
        wizard.SelectModel("asr", "kotoba-whisper-v2.2-faster");
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();
        Assert.True(wizard.RunSmokeCheck().IsSucceeded);
        File.Delete(paths.ReviewModelPath);

        var completed = wizard.CompleteIfReady();

        Assert.False(completed.IsCompleted);
        Assert.False(completed.LastSmokeSucceeded);
        Assert.Equal(SetupStep.SmokeTest, completed.CurrentStep);
    }

    [Fact]
    public void SetupWizard_RegisterSelectedLocalModel_RecordsChecksumManifestAndLicense()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();
        var modelPath = Path.Combine(paths.Root, "local-review.gguf");
        Touch(modelPath);
        File.WriteAllText($"{modelPath}.json", """{"model":"local-review"}""");

        var result = wizard.RegisterSelectedLocalModel("review", modelPath);
        var audit = Assert.Single(wizard.GetSelectedModelAudit());

        Assert.True(result.IsSucceeded);
        Assert.True(audit.ChecksumKnown);
        Assert.True(audit.ManifestKnown);
        Assert.True(audit.LicenseKnown);
    }

    [Fact]
    public void SetupWizard_ImportOfflineModelPack_InstallsWithoutBreakingBody()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths);
        wizard.AcceptLicenses();
        var packPath = CreateOfflinePack(paths, "kotoba-whisper-v2.2-faster", "model.bin");

        var result = wizard.ImportOfflineModelPack(packPath);

        Assert.True(result.IsSucceeded);
        Assert.Contains(result.InstalledModels, model => model.ModelId == "kotoba-whisper-v2.2-faster");
        Assert.NotNull(new InstalledModelRepository(paths).FindInstalledModel("kotoba-whisper-v2.2-faster")?.ManifestPath);
    }

    [Fact]
    public void SetupWizard_ImportOfflineModelPack_UsesInstallScopeDefaultStorage()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var wizard = CreateWizard(paths);
        wizard.AcceptLicenses();
        var packPath = CreateOfflinePack(paths, "kotoba-whisper-v2.2-faster", "model.bin");

        var result = wizard.ImportOfflineModelPack(packPath);

        var installed = Assert.Single(result.InstalledModels);
        Assert.True(result.IsSucceeded);
        Assert.StartsWith(paths.MachineModels, installed.FilePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelInstallService_DefaultInstallPath_UsesInstallScopeDefaultStorage()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogItem = new ModelCatalogService(paths)
            .LoadBuiltInCatalog()
            .Models
            .First(model => model.ModelId == "faster-whisper-large-v3");
        var service = new ModelInstallService(
            paths,
            new InstalledModelRepository(paths),
            new ModelVerificationService());

        var installPath = service.GetDefaultInstallPath(catalogItem);

        Assert.StartsWith(paths.MachineModels, installPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelInstallService_DefaultInstallPath_UsesProvidedStorageRoot()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var customRoot = Path.Combine(paths.Root, "custom-models");
        var catalogItem = new ModelCatalogService(paths)
            .LoadBuiltInCatalog()
            .Models
            .First(model => model.ModelId == "llm-jp-4-8b-thinking-q4-k-m");
        var service = new ModelInstallService(
            paths,
            new InstalledModelRepository(paths),
            new ModelVerificationService());

        var installPath = service.GetDefaultInstallPath(catalogItem, customRoot);

        Assert.StartsWith(customRoot, installPath, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(
            Path.Combine("review", catalogItem.ModelId, "llm-jp-4-8B-thinking-Q4_K_M.gguf"),
            installPath,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ModelInstallService_DefaultInstallPath_PutsReviewModelUnderModelDirectory()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var catalogItem = new ModelCatalogService(paths)
            .LoadBuiltInCatalog()
            .Models
            .First(model => model.ModelId == "llm-jp-4-8b-thinking-q4-k-m");
        var service = new ModelInstallService(
            paths,
            new InstalledModelRepository(paths),
            new ModelVerificationService());

        var installPath = service.GetDefaultInstallPath(catalogItem);

        Assert.Equal(
            Path.Combine(paths.UserModels, "review", catalogItem.ModelId, "llm-jp-4-8B-thinking-Q4_K_M.gguf"),
            installPath);
    }

    [Fact]
    public async Task SetupWizard_DownloadFailure_ReturnsFailureAndKeepsSetupIncomplete()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, new HttpClient(new FailingHandler()));
        wizard.SelectModel("asr", "faster-whisper-large-v3");
        wizard.AcceptLicenses();

        var result = await wizard.DownloadSelectedModelAsync("asr");

        Assert.False(result.IsSucceeded);
        Assert.False(wizard.LoadState().IsCompleted);
        Assert.Equal(SetupStep.Install, wizard.LoadState().CurrentStep);
    }

    [Fact]
    public async Task SetupWizard_InstallCudaReviewRuntime_DelegatesToRuntimeInstaller()
    {
        var paths = CreatePaths();
        Touch(paths.LlamaCompletionPath);
        var archive = CreateCudaRuntimeArchive();
        var cudaService = new CudaReviewRuntimeService(
            paths,
            new HttpClient(new ArchiveHandler(archive)),
            new CudaReviewRuntimeOptions(
                "https://example.test/redist.json",
                "https://example.test/redist/",
                LegacyRuntimeUrl: "https://example.test/cuda-review-runtime.zip"));
        var wizard = CreateWizard(paths, cudaReviewRuntimeService: cudaService);

        var result = await wizard.InstallCudaReviewRuntimeAsync();

        Assert.True(result.IsSucceeded);
        Assert.True(CudaReviewRuntimeLayout.HasPackage(paths));
        Assert.True(File.Exists(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll")));
    }

    private static SetupWizardService CreateWizard(
        AppPaths paths,
        HttpClient? httpClient = null,
        ISetupHostResourceProbe? hostResourceProbe = null,
        CudaReviewRuntimeService? cudaReviewRuntimeService = null)
    {
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModelRepository = new InstalledModelRepository(paths);
        var verificationService = new ModelVerificationService();
        var catalogService = new ModelCatalogService(paths);
        var installService = new ModelInstallService(paths, installedModelRepository, verificationService);
        return new SetupWizardService(
            paths,
            new SetupStateService(paths),
            new ToolStatusService(paths),
            catalogService,
            installedModelRepository,
            installService,
            new ModelPackImportService(paths, catalogService, installService),
            new ModelDownloadService(
                httpClient ?? new HttpClient(),
                new ModelDownloadJobRepository(paths),
                verificationService,
                installService),
            new FasterWhisperRuntimeService(paths, new ExternalProcessRunner()),
            new DiarizationRuntimeService(paths, new ExternalProcessRunner()),
            hostResourceProbe ?? new FixedHostResourceProbe(null, null, nvidiaGpuDetected: false),
            cudaReviewRuntimeService: cudaReviewRuntimeService);
    }

    private static AppPaths CreatePaths(InstallScope installScope = InstallScope.CurrentUser)
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return installScope == InstallScope.CurrentUser
            ? new AppPaths(root, root, AppContext.BaseDirectory)
            : new AppPaths(new AppPathOptions(
                AppDataRoot: root,
                LocalAppDataRoot: Path.Combine(root, "local"),
                ProgramDataRoot: Path.Combine(root, "program-data"),
                AppBaseDirectory: AppContext.BaseDirectory,
                InstallScope: installScope));
    }

    private static AppPaths CreatePathsWithoutTernaryRuntime()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var appBase = Path.Combine(root, "app");
        Directory.CreateDirectory(Path.Combine(appBase, "catalog"));
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "catalog", "model-catalog.json"),
            Path.Combine(appBase, "catalog", "model-catalog.json"));
        return new AppPaths(root, root, appBase);
    }

    private static void UpsertVerified(InstalledModelRepository repository, string modelId, string role, string engineId, string filePath)
    {
        repository.UpsertInstalledModel(new InstalledModel(
            modelId,
            role,
            engineId,
            modelId,
            Family: null,
            Version: null,
            filePath,
            ManifestPath: null,
            SizeBytes: 0,
            Sha256: null,
            Verified: true,
            LicenseName: "test",
            SourceType: "test",
            InstalledAt: DateTimeOffset.Now,
            LastVerifiedAt: DateTimeOffset.Now,
            Status: "installed"));
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    private static string CreateOfflinePack(AppPaths paths, string modelId, string relativePath)
    {
        var packRoot = Path.Combine(paths.Root, "pack-src");
        Directory.CreateDirectory(packRoot);
        var modelPath = Path.Combine(packRoot, relativePath);
        Touch(modelPath);
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(modelPath))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(packRoot, "modelpack.json"), $$"""
            {
              "schema_version": 1,
              "pack_id": "test-pack",
              "display_name": "Test pack",
              "models": [
                {
                  "model_id": "{{modelId}}",
                  "engine_id": "whisper-base",
                  "relative_path": "{{relativePath.Replace("\\", "\\\\")}}",
                  "sha256": "{{sha256}}"
                }
              ]
            }
            """);

        var packPath = Path.Combine(paths.Root, "test.kmodelpack");
        if (File.Exists(packPath))
        {
            File.Delete(packPath);
        }

        ZipFile.CreateFromDirectory(packRoot, packPath);
        return packPath;
    }

    private static byte[] CreateCudaRuntimeArchive()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddArchiveEntry(archive, "bin/ggml-cuda.dll", "cuda");
            AddArchiveEntry(archive, "bin/cublas64_12.dll", "cublas");
            AddArchiveEntry(archive, "bin/cublasLt64_12.dll", "cublasLt");
            AddArchiveEntry(archive, "bin/cudart64_12.dll", "cudart");
            AddArchiveEntry(archive, "bin/cudnn64_9.dll", "cudnn");
            AddArchiveEntry(archive, "bin/crispasr.exe", "crisp exe");
            AddArchiveEntry(archive, "bin/crispasr.dll", "crisp dll");
            AddArchiveEntry(archive, "bin/whisper.dll", "whisper");
        }

        return stream.ToArray();
    }

    private static void AddArchiveEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static void CreateDiarizationRuntime(AppPaths paths)
    {
        Directory.CreateDirectory(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "diarize-0.1.2.dist-info"));
        Touch(Path.Combine(paths.DiarizationPythonEnvironment, "Lib", "site-packages", "silero_vad", "data", "silero_vad.jit"));
    }

    private static void CreateFasterWhisperRuntime(AppPaths paths)
    {
        Touch(paths.AsrPythonPath);
        Touch(paths.FasterWhisperScriptPath);
        Directory.CreateDirectory(Path.Combine(paths.AsrPythonEnvironment, "Lib", "site-packages", "faster_whisper"));
    }

    private static void CreateCudaReviewRuntime(AppPaths paths)
    {
        Touch(Path.Combine(paths.ReviewRuntimeDirectory, "ggml-cuda.dll"));
        Touch(Path.Combine(paths.ReviewRuntimeDirectory, "cublas64_12.dll"));
        Touch(Path.Combine(paths.ReviewRuntimeDirectory, "cublasLt64_12.dll"));
        Touch(Path.Combine(paths.ReviewRuntimeDirectory, "cudart64_12.dll"));
        Touch(paths.CudaReviewRuntimeMarkerPath);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }

    private sealed class ArchiveHandler(byte[] archive) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(archive)
            });
        }
    }

    private sealed class FixedHostResourceProbe(
        long? totalMemoryBytes,
        int? maxGpuMemoryGb,
        bool nvidiaGpuDetected,
        int? logicalProcessorCount = null) : ISetupHostResourceProbe
    {
        public SetupHostResources GetResources()
        {
            return new SetupHostResources(
                totalMemoryBytes,
                maxGpuMemoryGb,
                nvidiaGpuDetected,
                logicalProcessorCount,
                $"RAM {totalMemoryBytes} / CPU {logicalProcessorCount} / VRAM {maxGpuMemoryGb}");
        }
    }
}
