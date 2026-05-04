using System.Text.Json;
using System.IO.Compression;
using System.Net;
using KoeNote.App.Services;
using KoeNote.App.Services.Models;
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
    public void SetupStateService_AllUsersScope_DefaultsStorageToMachineModels()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var service = new SetupStateService(paths);

        var initial = service.Load();

        Assert.Equal(paths.MachineModels, initial.StorageRoot);
    }

    [Fact]
    public void SetupWizard_ListsReazonSpeechFasterWhisperAndReviewChoices()
    {
        var wizard = CreateWizard(CreatePaths());

        var asrModels = wizard.GetSelectableModels("asr");
        var reviewModels = wizard.GetSelectableModels("review");

        Assert.Contains(asrModels, entry => entry.DisplayName.Contains("ReazonSpeech", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(asrModels, entry => entry.DisplayName.Contains("faster-whisper", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(reviewModels);
    }

    [Fact]
    public void SetupWizard_RecommendedSelections_UseInstallScopeDefaultStorage()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var wizard = CreateWizard(paths);

        var state = wizard.UseRecommendedSelections();

        Assert.Equal(paths.MachineModels, state.StorageRoot);
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
    public void SetupWizard_CompletesAfterVerifiedModelsAndSmokePass()
    {
        var paths = CreatePaths();
        Touch(paths.FfmpegPath);
        Touch(paths.CrispAsrPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.VibeVoiceAsrModelPath);
        Touch(paths.ReviewModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "vibevoice-asr-q4-k", "asr", "vibevoice-asr-gguf", paths.VibeVoiceAsrModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths);
        wizard.SelectModel("asr", "vibevoice-asr-q4-k");
        wizard.SelectModel("review", "llm-jp-4-8b-thinking-q4-k-m");
        wizard.AcceptLicenses();

        var smoke = wizard.RunSmokeCheck();
        var completed = wizard.CompleteIfReady();

        Assert.True(smoke.IsSucceeded);
        Assert.True(completed.IsCompleted);
        Assert.Equal(SetupStep.Complete, completed.CurrentStep);
    }

    [Fact]
    public void SetupWizard_CompleteIfReady_RechecksCurrentModelFiles()
    {
        var paths = CreatePaths();
        Touch(paths.FfmpegPath);
        Touch(paths.CrispAsrPath);
        Touch(paths.LlamaCompletionPath);
        Touch(paths.VibeVoiceAsrModelPath);
        Touch(paths.ReviewModelPath);
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var installedModels = new InstalledModelRepository(paths);
        UpsertVerified(installedModels, "vibevoice-asr-q4-k", "asr", "vibevoice-asr-gguf", paths.VibeVoiceAsrModelPath);
        UpsertVerified(installedModels, "llm-jp-4-8b-thinking-q4-k-m", "review", "llm-jp-gguf", paths.ReviewModelPath);
        var wizard = CreateWizard(paths);
        wizard.SelectModel("asr", "vibevoice-asr-q4-k");
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
        var packPath = CreateOfflinePack(paths, "vibevoice-asr-q4-k", "model.bin");

        var result = wizard.ImportOfflineModelPack(packPath);

        Assert.True(result.IsSucceeded);
        Assert.Contains(result.InstalledModels, model => model.ModelId == "vibevoice-asr-q4-k");
        Assert.NotNull(new InstalledModelRepository(paths).FindInstalledModel("vibevoice-asr-q4-k")?.ManifestPath);
    }

    [Fact]
    public void SetupWizard_ImportOfflineModelPack_UsesInstallScopeDefaultStorage()
    {
        var paths = CreatePaths(InstallScope.AllUsers);
        var wizard = CreateWizard(paths);
        var packPath = CreateOfflinePack(paths, "vibevoice-asr-q4-k", "model.bin");

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
    public async Task SetupWizard_DownloadFailure_ReturnsFailureAndKeepsSetupIncomplete()
    {
        var paths = CreatePaths();
        var wizard = CreateWizard(paths, new HttpClient(new FailingHandler()));
        wizard.SelectModel("asr", "faster-whisper-large-v3");

        var result = await wizard.DownloadSelectedModelAsync("asr");

        Assert.False(result.IsSucceeded);
        Assert.False(wizard.LoadState().IsCompleted);
        Assert.Equal(SetupStep.Install, wizard.LoadState().CurrentStep);
    }

    private static SetupWizardService CreateWizard(AppPaths paths, HttpClient? httpClient = null)
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
                installService));
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
                  "engine_id": "vibevoice-crispasr",
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

    private sealed class FailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
