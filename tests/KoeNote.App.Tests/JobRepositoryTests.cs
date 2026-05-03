using KoeNote.App.Services;
using KoeNote.App.Services.Jobs;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Tests;

public sealed class JobRepositoryTests
{
    [Fact]
    public void CreateFromAudio_PersistsJobWithLongJapaneseFileName()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repository = new JobRepository(paths);
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            "これは非常に長い日本語ファイル名の会議録音サンプルです_株式会社テスト_開発定例_2026年05月03日.m4a");

        var job = repository.CreateFromAudio(sourcePath);

        Assert.Contains("非常に長い日本語ファイル名", job.FileName);

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT title, source_audio_path, status FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Contains("非常に長い日本語ファイル名", reader.GetString(0));
        Assert.Equal(sourcePath, reader.GetString(1));
        Assert.Equal("登録済み", reader.GetString(2));
    }

    [Fact]
    public void UpdatePreprocessResult_PersistsNormalizedAudioAndProgress()
    {
        var paths = CreatePaths();
        paths.EnsureCreated();
        new DatabaseInitializer(paths).EnsureCreated();

        var repository = new JobRepository(paths);
        var job = repository.CreateFromAudio(@"C:\audio\meeting.wav");

        repository.UpdatePreprocessResult(job, "音声変換完了", "preprocessed", 100, @"C:\normalized\audio.wav");

        using var connection = Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, current_stage, progress_percent, normalized_audio_path FROM jobs WHERE job_id = $job_id;";
        command.Parameters.AddWithValue("$job_id", job.JobId);

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("音声変換完了", reader.GetString(0));
        Assert.Equal("preprocessed", reader.GetString(1));
        Assert.Equal(100, reader.GetInt32(2));
        Assert.Equal(@"C:\normalized\audio.wav", reader.GetString(3));
    }

    private static AppPaths CreatePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        var local = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"));
        return new AppPaths(root, local);
    }

    private static SqliteConnection Open(AppPaths paths)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath
        }.ToString());
        connection.Open();
        return connection;
    }
}
