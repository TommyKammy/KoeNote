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

    [Fact]
    public void CanDownloadAndRetry_RequireSelectableEntry()
    {
        var presenter = new ModelCatalogPresenter();
        var selectable = CreateEntry(
            "selectable",
            downloadType: "direct",
            downloadUrl: "https://example.com/selectable.gguf");
        var hidden = CreateEntry(
            "hidden",
            status: "hidden",
            downloadType: "direct",
            downloadUrl: "https://example.com/hidden.gguf");
        var hiddenFailed = CreateEntry(
            "hidden-failed",
            status: "hidden",
            downloadType: "direct",
            downloadUrl: "https://example.com/hidden-failed.gguf",
            latestDownloadJob: CreateDownloadJob("hidden-failed", "failed"));
        var selectablePaused = CreateEntry(
            "selectable-paused",
            downloadType: "direct",
            downloadUrl: "https://example.com/selectable-paused.gguf",
            latestDownloadJob: CreateDownloadJob("selectable-paused", "paused"));
        var hiddenPaused = CreateEntry(
            "hidden-paused",
            status: "hidden",
            downloadType: "direct",
            downloadUrl: "https://example.com/hidden-paused.gguf",
            latestDownloadJob: CreateDownloadJob("hidden-paused", "paused"));

        Assert.True(presenter.CanDownload(selectable));
        Assert.False(presenter.CanDownload(hidden));
        Assert.False(presenter.CanRetry(hiddenFailed));
        Assert.True(presenter.CanResume(selectablePaused));
        Assert.False(presenter.CanResume(hiddenPaused));
        Assert.True(presenter.CanCancel(hiddenPaused));
    }

    private static ModelCatalogEntry CreateEntry(
        string modelId,
        string role = "asr",
        string engineId = "whisper-small",
        InstalledModel? installedModel = null,
        string status = "available",
        string downloadType = "manual",
        string? downloadUrl = null,
        ModelDownloadJob? latestDownloadJob = null)
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
                new ModelDownloadSpec(downloadType, downloadUrl, null),
                new ModelLicenseSpec("test", null),
                new ModelRequirements(false, 0, false),
                status),
            installedModel,
            latestDownloadJob);
    }

    private static ModelDownloadJob CreateDownloadJob(string modelId, string status)
    {
        return new ModelDownloadJob(
            Guid.NewGuid().ToString("N"),
            modelId,
            "https://example.com/model.gguf",
            Path.Combine(Path.GetTempPath(), "model.gguf"),
            Path.Combine(Path.GetTempPath(), "model.gguf.partial"),
            status,
            BytesTotal: null,
            BytesDownloaded: 0,
            Sha256Expected: null,
            Sha256Actual: null,
            ErrorMessage: null,
            CreatedAt: DateTimeOffset.Now,
            UpdatedAt: DateTimeOffset.Now);
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
