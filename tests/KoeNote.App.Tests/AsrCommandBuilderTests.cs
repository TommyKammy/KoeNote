using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrCommandBuilderTests
{
    [Fact]
    public void BuildArguments_IncludesCrispAsrVibeVoiceArguments()
    {
        var builder = new AsrCommandBuilder();
        var options = new AsrRunOptions(
            "job-001",
            @"C:\audio files\normalized.wav",
            "crispasr.exe",
            @"C:\models\vibevoice-asr-q4_k.gguf",
            @"C:\out",
            ["KoeNote", "RTX 3060"],
            "meeting context");

        var arguments = builder.BuildArguments(options);

        Assert.Contains("--backend vibevoice", arguments);
        Assert.Contains("--gpu-backend cuda", arguments);
        Assert.Contains("--model C:\\models\\vibevoice-asr-q4_k.gguf", arguments);
        Assert.Contains("--file \"C:\\audio files\\normalized.wav\"", arguments);
        Assert.Contains("--language ja", arguments);
        Assert.Contains("--no-punctuation", arguments);
        Assert.Contains("--output-json", arguments);
        Assert.Contains("--output-file C:\\out\\crispasr", arguments);
        Assert.Contains("--prompt", arguments);
        Assert.Contains("meeting context", arguments);
        Assert.Contains("KoeNote", arguments);
        Assert.Contains("RTX 3060", arguments);
    }

    [Fact]
    public void BuildArgumentList_PreservesArgumentsWithoutShellQuoting()
    {
        var builder = new AsrCommandBuilder();
        var options = new AsrRunOptions(
            "job-001",
            @"C:\audio files\normalized.wav",
            "crispasr.exe",
            @"C:\models\vibevoice-asr-q4_k.gguf",
            @"C:\out",
            ["RTX 3060"],
            "meeting context");

        var arguments = builder.BuildArgumentList(options);

        Assert.Equal(
            [
                "--backend",
                "vibevoice",
                "--gpu-backend",
                "cuda",
                "--model",
                @"C:\models\vibevoice-asr-q4_k.gguf",
                "--file",
                @"C:\audio files\normalized.wav",
                "--language",
                "ja",
                "--no-punctuation",
                "--output-json",
                "--output-file",
                @"C:\out\crispasr",
                "--prompt",
                $"meeting context{Environment.NewLine}Keywords: RTX 3060"
            ],
            arguments);
    }
}
