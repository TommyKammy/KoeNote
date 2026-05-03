using System.IO;
using System.Text;
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
        var rawPath = SaveRawOutput(outputDirectory, rawOutput);
        var normalizedPath = SaveNormalizedSegments(outputDirectory, segments);

        return (rawPath, normalizedPath);
    }

    public string SaveRawOutput(string outputDirectory, string rawOutput)
    {
        Directory.CreateDirectory(outputDirectory);

        var rawPath = Path.Combine(outputDirectory, "asr.raw.json");
        File.WriteAllText(rawPath, rawOutput, Encoding.UTF8);
        return rawPath;
    }

    public string SaveNormalizedSegments(string outputDirectory, IReadOnlyList<TranscriptSegment> segments)
    {
        Directory.CreateDirectory(outputDirectory);

        var normalizedPath = Path.Combine(outputDirectory, "segments.normalized.json");
        File.WriteAllText(normalizedPath, JsonSerializer.Serialize(segments, JsonOptions), Encoding.UTF8);
        return normalizedPath;
    }
}
