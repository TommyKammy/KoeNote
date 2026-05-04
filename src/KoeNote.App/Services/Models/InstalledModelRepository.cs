using Microsoft.Data.Sqlite;

namespace KoeNote.App.Services.Models;

public sealed class InstalledModelRepository(AppPaths paths)
{
    public IReadOnlyList<InstalledModel> ListInstalledModels()
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_id, role, engine_id, display_name, family, version, file_path, manifest_path,
                   size_bytes, sha256, verified, license_name, source_type, installed_at, last_verified_at, status
            FROM installed_models
            ORDER BY role, display_name;
            """;

        using var reader = command.ExecuteReader();
        var models = new List<InstalledModel>();
        while (reader.Read())
        {
            models.Add(ReadInstalledModel(reader));
        }

        return models;
    }

    public InstalledModel? FindInstalledModel(string modelId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT model_id, role, engine_id, display_name, family, version, file_path, manifest_path,
                   size_bytes, sha256, verified, license_name, source_type, installed_at, last_verified_at, status
            FROM installed_models
            WHERE model_id = $model_id;
            """;
        command.Parameters.AddWithValue("$model_id", modelId);

        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadInstalledModel(reader) : null;
    }

    public void UpsertInstalledModel(InstalledModel model)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO installed_models (
                model_id, role, engine_id, display_name, family, version, file_path, manifest_path,
                size_bytes, sha256, verified, license_name, source_type, installed_at, last_verified_at, status
            )
            VALUES (
                $model_id, $role, $engine_id, $display_name, $family, $version, $file_path, $manifest_path,
                $size_bytes, $sha256, $verified, $license_name, $source_type, $installed_at, $last_verified_at, $status
            )
            ON CONFLICT(model_id) DO UPDATE SET
                role = excluded.role,
                engine_id = excluded.engine_id,
                display_name = excluded.display_name,
                family = excluded.family,
                version = excluded.version,
                file_path = excluded.file_path,
                manifest_path = excluded.manifest_path,
                size_bytes = excluded.size_bytes,
                sha256 = excluded.sha256,
                verified = excluded.verified,
                license_name = excluded.license_name,
                source_type = excluded.source_type,
                last_verified_at = excluded.last_verified_at,
                status = excluded.status;
            """;
        AddParameters(command, model);
        command.ExecuteNonQuery();
    }

    public void DeleteInstalledModel(string modelId)
    {
        using var connection = SqliteConnectionFactory.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM installed_models WHERE model_id = $model_id;";
        command.Parameters.AddWithValue("$model_id", modelId);
        command.ExecuteNonQuery();
    }

    private static InstalledModel ReadInstalledModel(SqliteDataReader reader)
    {
        return new InstalledModel(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetInt64(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.GetInt32(10) == 1,
            reader.IsDBNull(11) ? null : reader.GetString(11),
            reader.GetString(12),
            DateTimeOffset.Parse(reader.GetString(13)),
            reader.IsDBNull(14) ? null : DateTimeOffset.Parse(reader.GetString(14)),
            reader.GetString(15));
    }

    private static void AddParameters(SqliteCommand command, InstalledModel model)
    {
        command.Parameters.AddWithValue("$model_id", model.ModelId);
        command.Parameters.AddWithValue("$role", model.Role);
        command.Parameters.AddWithValue("$engine_id", model.EngineId);
        command.Parameters.AddWithValue("$display_name", model.DisplayName);
        command.Parameters.AddWithValue("$family", (object?)model.Family ?? DBNull.Value);
        command.Parameters.AddWithValue("$version", (object?)model.Version ?? DBNull.Value);
        command.Parameters.AddWithValue("$file_path", model.FilePath);
        command.Parameters.AddWithValue("$manifest_path", (object?)model.ManifestPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$size_bytes", (object?)model.SizeBytes ?? DBNull.Value);
        command.Parameters.AddWithValue("$sha256", (object?)model.Sha256 ?? DBNull.Value);
        command.Parameters.AddWithValue("$verified", model.Verified ? 1 : 0);
        command.Parameters.AddWithValue("$license_name", (object?)model.LicenseName ?? DBNull.Value);
        command.Parameters.AddWithValue("$source_type", model.SourceType);
        command.Parameters.AddWithValue("$installed_at", model.InstalledAt.ToString("o"));
        command.Parameters.AddWithValue("$last_verified_at", (object?)model.LastVerifiedAt?.ToString("o") ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", model.Status);
    }
}
