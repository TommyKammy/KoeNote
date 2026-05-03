using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using KoeNote.App.Models;

namespace KoeNote.App.Services.Asr;

public sealed class AsrResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public (string RawOutputPath, string NormalizedSegmentsPath) Save(
        string outputDirectory,
        string rawOutput,
        IReadOnlyList<TranscriptSegment> segments)
    {
        Directory.CreateDirectory(outputDirectory);

        var rawPath = Path.Combine(outputDirectory, "asr.raw.json");
        var normalizedPath = Path.Combine(outputDirectory, "segments.normalized.json");

        File.WriteAllText(rawPath, rawOutput);
        File.WriteAllText(normalizedPath, JsonSerializer.Serialize(segments, JsonOptions));

        return (rawPath, normalizedPath);
    }
}
