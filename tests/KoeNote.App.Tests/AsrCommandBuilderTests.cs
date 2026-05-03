using KoeNote.App.Services.Asr;

namespace KoeNote.App.Tests;

public sealed class AsrCommandBuilderTests
{
    [Fact]
    public void BuildArguments_IncludesModelAudioContextAndHotwords()
    {
        var builder = new AsrCommandBuilder();
        var options = new AsrRunOptions(
            "job-001",
            @"C:\audio files\normalized.wav",
            "crispasr.exe",
            @"C:\models\vibevoice-asr-q4_k.gguf",
            @"C:\out",
            ["KoeNote", "RTX 3060"],
            "製品開発会議");

        var arguments = builder.BuildArguments(options);

        Assert.Contains("--model \"C:\\models\\vibevoice-asr-q4_k.gguf\"", arguments);
        Assert.Contains("--audio \"C:\\audio files\\normalized.wav\"", arguments);
        Assert.Contains("--format \"json\"", arguments);
        Assert.Contains("--context \"製品開発会議\"", arguments);
        Assert.Contains("--hotword \"KoeNote\"", arguments);
        Assert.Contains("--hotword \"RTX 3060\"", arguments);
    }
}
