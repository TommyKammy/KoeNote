using System.IO;
using System.Text.Json;

namespace KoeNote.App.Services;

public enum MainLayoutMode
{
    Standard,
    Detail
}

public sealed record UiPreferences(
    double MainContentZoomScale = 1.0,
    MainLayoutMode MainLayoutMode = MainLayoutMode.Standard);

public sealed class UiPreferencesService(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path = Path.Combine(paths.Root, "ui-preferences.json");

    public UiPreferences Load()
    {
        if (!File.Exists(_path))
        {
            return new UiPreferences();
        }

        try
        {
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<UiPreferences>(json, JsonOptions) ?? new UiPreferences();
        }
        catch (JsonException)
        {
            return new UiPreferences();
        }
        catch (IOException)
        {
            return new UiPreferences();
        }
    }

    public void Save(UiPreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(preferences, JsonOptions));
    }

    public void SaveMainContentZoomScale(double scale)
    {
        Save(Load() with { MainContentZoomScale = scale });
    }

    public void SaveMainLayoutMode(MainLayoutMode mode)
    {
        Save(Load() with { MainLayoutMode = NormalizeMainLayoutMode(mode) });
    }

    public static MainLayoutMode NormalizeMainLayoutMode(MainLayoutMode mode)
    {
        return Enum.IsDefined(mode) ? mode : MainLayoutMode.Standard;
    }
}
