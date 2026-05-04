using KoeNote.App.Services.Models;

namespace KoeNote.App.Tests;

public sealed class ModelVerificationServiceTests
{
    [Fact]
    public void VerifyPath_FailsWhenSha256DoesNotMatch()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "model.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "model");

        var result = new ModelVerificationService().VerifyPath(path, new string('0', 64));

        Assert.False(result.IsVerified);
        Assert.Equal("Checksum mismatch.", result.Message);
        Assert.NotNull(result.Sha256);
    }
}
