using KoeNote.App.Services.Jobs;

namespace KoeNote.App.Tests;

public sealed class StageProgressRepositoryTests
{
    [Fact]
    public void Upsert_InsertsAndUpdatesStageProgress()
    {
        var fixture = TestDatabase.CreateRepositoryFixture();
        var paths = fixture.Paths;

        var repository = new StageProgressRepository(paths);
        repository.Upsert("job-001", "preprocess", "running", 10);
        repository.Upsert("job-001", "preprocess", "succeeded", 100, exitCode: 0, logPath: @"C:\logs\preprocess.log");

        using var connection = fixture.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, progress_percent, exit_code, log_path FROM stage_progress WHERE stage_id = 'job-001-preprocess';";

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("succeeded", reader.GetString(0));
        Assert.Equal(100, reader.GetInt32(1));
        Assert.Equal(0, reader.GetInt32(2));
        Assert.Equal(@"C:\logs\preprocess.log", reader.GetString(3));
        Assert.False(reader.Read());
    }

}
