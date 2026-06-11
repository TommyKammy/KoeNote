using System.IO;
using System.Text.Json;

namespace KoeNote.App.Services.Presets;

internal sealed class DomainPresetParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public DomainPreset Load(string presetPath)
    {
        if (string.IsNullOrWhiteSpace(presetPath))
        {
            throw new ArgumentException("プリセットファイルを指定してください。", nameof(presetPath));
        }

        if (!File.Exists(presetPath))
        {
            throw new FileNotFoundException("プリセットファイルが見つかりません。", presetPath);
        }

        var preset = JsonSerializer.Deserialize<DomainPreset>(File.ReadAllText(presetPath), JsonOptions)
            ?? throw new InvalidDataException("プリセットJSONを読み込めませんでした。");
        preset.Validate();
        return preset;
    }

    public DomainPreset? TryLoad(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return null;
            }

            var preset = JsonSerializer.Deserialize<DomainPreset>(File.ReadAllText(sourcePath), JsonOptions);
            preset?.Validate();
            return preset;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            return null;
        }
    }
}
