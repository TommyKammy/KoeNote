using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using KoeNote.App.Models;

namespace KoeNote.App.Services.Review;

public sealed class ReviewResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    public string SaveRawOutput(string outputDirectory, string rawOutput, string fileName = "review.raw.json")
    {
        Directory.CreateDirectory(outputDirectory);

        var rawPath = Path.Combine(outputDirectory, fileName);
        File.WriteAllText(rawPath, rawOutput, Encoding.UTF8);
        return rawPath;
    }

    public string SaveNormalizedDrafts(string outputDirectory, IReadOnlyList<CorrectionDraft> drafts)
    {
        Directory.CreateDirectory(outputDirectory);

        var normalizedPath = Path.Combine(outputDirectory, "correction_drafts.normalized.json");
        File.WriteAllText(normalizedPath, JsonSerializer.Serialize(drafts, JsonOptions), Encoding.UTF8);
        return normalizedPath;
    }
}
