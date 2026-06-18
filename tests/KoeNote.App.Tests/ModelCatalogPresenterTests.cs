using KoeNote.App.Models;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Models;
using KoeNote.App.ViewModels;

namespace KoeNote.App.Tests;

public sealed class ModelCatalogPresenterTests
{
    [Fact]
    public void RefreshEntries_ReplacesEntriesAndPreservesPreferredSelection()
    {
        var presenter = new ModelCatalogPresenter();
        var original = CreateEntry("original");
        var preferred = CreateEntry("preferred");
        var entries = new List<ModelCatalogEntry> { original };

        var selected = presenter.RefreshEntries(entries, [original, preferred], "preferred");

        Assert.Equal([original, preferred], entries);
        Assert.Same(preferred, selected);
    }

    [Fact]
    public void BuildAsrEngineOptions_FiltersSelectableEnginesAndMarksInstalled()
    {
        var presenter = new ModelCatalogPresenter();
        var engines = new IAsrEngine[]
        {
            new TestAsrEngine("faster-whisper-large-v3-turbo", "Turbo"),
            new TestAsrEngine("reazonspeech-k2-v3", "ReazonSpeech"),
            new TestAsrEngine("whisper-small", "Whisper Small")
        };

        var options = presenter.BuildAsrEngineOptions(
            engines,
            engineId => string.Equals(engineId, "whisper-small", StringComparison.OrdinalIgnoreCase));

        Assert.Collection(
            options,
            option =>
            {
                Assert.Equal("faster-whisper-large-v3-turbo", option.EngineId);
                Assert.False(option.IsInstalled);
            },
            option =>
            {
                Assert.Equal("whisper-small", option.EngineId);
                Assert.True(option.IsInstalled);
            });
    }

    [Fact]
    public void FindInstalledEntry_RequiresVerifiedInstalledMatchingRole()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var installedPath = Path.Combine(root, "installed.bin");
        File.WriteAllText(installedPath, "model");
        var match = CreateEntry(
            "match",
            role: "asr",
            engineId: "whisper-small",
            installedModel: CreateInstalled("match", "asr", "whisper-small", installedPath, verified: true));
        var unverified = CreateEntry(
            "unverified",
            role: "asr",
            engineId: "whisper-base",
            installedModel: CreateInstalled("unverified", "asr", "whisper-base", installedPath, verified: false));
        var wrongRole = CreateEntry(
            "wrong-role",
            role: "review",
            engineId: "llama-cpp",
            installedModel: CreateInstalled("wrong-role", "review", "llama-cpp", installedPath, verified: true));

        var found = new ModelCatalogPresenter().FindInstalledEntry(
            [unverified, wrongRole, match],
            "asr",
            entry => entry.EngineId == "whisper-small");

        Assert.Same(match, found);
    }

    private static ModelCatalogEntry CreateEntry(
        string modelId,
        string role = "asr",
        string engineId = "whisper-small",
        InstalledModel? installedModel = null)
    {
        return new ModelCatalogEntry(
            new ModelCatalogItem(
                modelId,
                "test",
                role,
                engineId,
                modelId,
                [],
                [],
                new ModelRuntimeSpec("none", "none"),
                new ModelDownloadSpec("manual", null, null),
                new ModelLicenseSpec("test", null),
                new ModelRequirements(false, 0, false),
                "available"),
            installedModel);
    }

    private static InstalledModel CreateInstalled(
        string modelId,
        string role,
        string engineId,
        string filePath,
        bool verified)
    {
        return new InstalledModel(
            modelId,
            role,
            engineId,
            modelId,
            Family: null,
            Version: null,
            filePath,
            ManifestPath: null,
            SizeBytes: null,
            Sha256: null,
            verified,
            LicenseName: null,
            SourceType: "test",
            InstalledAt: DateTimeOffset.Now,
            LastVerifiedAt: verified ? DateTimeOffset.Now : null,
            Status: "installed");
    }

    private sealed class TestAsrEngine(string engineId, string displayName) : IAsrEngine
    {
        public string EngineId { get; } = engineId;

        public string DisplayName { get; } = displayName;

        public Task<AsrEngineCheckResult> CheckAsync(
            AsrEngineConfig config,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new AsrEngineCheckResult(true, []));
        }

        public Task<AsrResult> TranscribeAsync(
            AsrInput input,
            AsrEngineConfig config,
            AsrOptions options,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
