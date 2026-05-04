using System.IO;
using System.Text.Json;

namespace KoeNote.App.Services.Setup;

public sealed class SetupStateService(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public SetupState Load()
    {
        if (!File.Exists(paths.SetupStatePath))
        {
            return SetupState.Default(paths.UserModels);
        }

        try
        {
            var json = File.ReadAllText(paths.SetupStatePath);
            return JsonSerializer.Deserialize<SetupState>(json, JsonOptions) ?? SetupState.Default(paths.UserModels);
        }
        catch (JsonException)
        {
            return SetupState.Default(paths.UserModels);
        }
    }

    public SetupState Save(SetupState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(paths.SetupStatePath)!);
        var updated = state with { UpdatedAt = DateTimeOffset.Now };
        File.WriteAllText(paths.SetupStatePath, JsonSerializer.Serialize(updated, JsonOptions));
        return updated;
    }

    public SetupState Reset()
    {
        var state = SetupState.Default(paths.UserModels);
        return Save(state);
    }
}
