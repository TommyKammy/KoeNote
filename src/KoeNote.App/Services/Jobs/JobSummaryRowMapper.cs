using System.IO;
using KoeNote.App.Models;
using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Jobs;

public static class JobSummaryRowMapper
{
    public static JobSummary ReadSummary(
        SqliteDataReader reader,
        bool includeDeletedStorageBytes,
        Func<string, long> calculateJobStorageBytes)
    {
        var jobId = reader.GetString(0);
        var sourceAudioPath = reader.GetString(2);
        var createdAt = reader.GetDateTimeOffset(7);
        var updatedAt = reader.GetDateTimeOffset(8);

        return new JobSummary(
            jobId,
            reader.GetString(1),
            Path.GetFileName(sourceAudioPath),
            sourceAudioPath,
            NormalizeDisplayStatus(reader.GetString(4)),
            reader.GetInt32(5),
            reader.GetInt32(6),
            updatedAt,
            createdAt,
            reader.GetNullableString(3),
            reader.GetInt32(9) != 0,
            reader.GetNullableDateTimeOffset(10),
            reader.GetNullableString(11),
            includeDeletedStorageBytes ? calculateJobStorageBytes(jobId) : 0);
    }

    private static string NormalizeDisplayStatus(string status)
    {
        return status switch
        {
            "レビュー待ち" => "整文待ち",
            "レビュー完了" => "整文完了",
            "レビュー済み" => "整文済み",
            "レビュー中" => "整文中",
            "レビュー失敗" => "整文失敗",
            "推敲候補なし" => "整文候補なし",
            "推敲中" => "整文中",
            _ => status
        };
    }
}
