using System.Globalization;
using KoeNote.App.Converters;
using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class ModelCatalogEntryDisplayConverterTests
{
    [Fact]
    public void Convert_ReturnsSetupDisplayNameForModelCatalogEntry()
    {
        var converter = new ModelCatalogEntryDisplayConverter();
        var modelPath = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "gemma.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, "model");
        var entry = new ModelCatalogEntry(
            CreateCatalogItem("gemma-4-e4b-it-q4-k-m", "Gemma 4 E4B it Q4_K_M"),
            new InstalledModel(
                "gemma-4-e4b-it-q4-k-m",
                "review",
                "llama-cpp",
                "Gemma 4 E4B it Q4_K_M",
                "gemma",
                null,
                modelPath,
                null,
                null,
                null,
                true,
                "Apache-2.0",
                "download",
                DateTimeOffset.Now,
                DateTimeOffset.Now,
                "installed"));

        var display = converter.Convert(entry, typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Gemma 4 E4B it Q4_K_M (導入済み)", display);
    }

    [Fact]
    public void Convert_ReturnsSelectionBoxTextWhenValueIsString()
    {
        var converter = new ModelCatalogEntryDisplayConverter();

        var display = converter.Convert("Gemma 4 E4B it Q4_K_M (導入済み)", typeof(string), null, CultureInfo.InvariantCulture);

        Assert.Equal("Gemma 4 E4B it Q4_K_M (導入済み)", display);
    }

    private static ModelCatalogItem CreateCatalogItem(string modelId, string displayName)
    {
        return new ModelCatalogItem(
            modelId,
            "gemma",
            "review",
            "llama-cpp",
            displayName,
            ["ja"],
            [],
            new ModelRuntimeSpec("llama-cpp", "runtime-llama-cpp"),
            new ModelDownloadSpec("huggingface", "https://example.test/model.gguf", null),
            new ModelLicenseSpec("Apache-2.0", "https://example.test/license"),
            new ModelRequirements(false, 6, false),
            "available",
            null,
            []);
    }
}
