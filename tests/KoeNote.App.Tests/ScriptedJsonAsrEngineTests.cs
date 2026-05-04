using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class ScriptedJsonAsrEngineTests
{
    [Fact]
    public async Task TranscribeAsync_RejectsMissingModelBeforeStartingProcess()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();
        var scriptPath = Path.Combine(paths.Root, "worker.py");
        var audioPath = Path.Combine(paths.Root, "audio.wav");
        File.WriteAllText(scriptPath, "print('should not run')");
        File.WriteAllText(audioPath, "");
        var engine = CreateEngine(paths);

        var exception = await Assert.ThrowsAsync<AsrWorkerException>(() => engine.TranscribeAsync(
            new AsrInput("job-001", audioPath),
            new AsrEngineConfig(
                "python",
                Path.Combine(paths.Root, "missing-model"),
                Path.Combine(paths.Jobs, "job-001", "asr"),
                "missing-model",
                scriptPath),
            new AsrOptions()));

        Assert.Equal(AsrFailureCategory.MissingModel, exception.Category);
        Assert.Equal("failed", ReadSingleRunStatus(paths));
    }

    private static ScriptedJsonAsrEngine CreateEngine(AppPaths paths)
    {
        return new ScriptedJsonAsrEngine(
            "scripted-test",
            "Scripted test",
            "scripted",
            new ExternalProcessRunner(),
            new AsrJsonNormalizer(),
            new AsrResultStore(),
            new TranscriptSegmentRepository(paths),
            new AsrRunRepository(paths));
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
    }

    private static string ReadSingleRunStatus(AppPaths paths)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM asr_runs;";
        return Convert.ToString(command.ExecuteScalar()) ?? "";
    }
}
