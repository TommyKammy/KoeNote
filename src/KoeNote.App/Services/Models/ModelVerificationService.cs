using System.IO;
using System.Security.Cryptography;

namespace KoeNote.App.Services.Models;

public sealed record ModelVerificationResult(bool IsVerified, string? Sha256, string Message);

public sealed class ModelVerificationService
{
    public ModelVerificationResult VerifyPath(string path, string? expectedSha256)
    {
        if (File.Exists(path))
        {
            var actualSha256 = ComputeSha256(path);
            if (IsConcreteSha256(expectedSha256) &&
                !string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return new ModelVerificationResult(false, actualSha256, "Checksum mismatch.");
            }

            return new ModelVerificationResult(true, actualSha256, "Verified.");
        }

        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray();
            if (files.Length == 0)
            {
                return new ModelVerificationResult(false, null, "Model directory is empty.");
            }

            return new ModelVerificationResult(true, expectedSha256, "Directory exists.");
        }

        return new ModelVerificationResult(false, null, $"Model path not found: {path}");
    }

    public static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool IsConcreteSha256(string? sha256)
    {
        return !string.IsNullOrWhiteSpace(sha256)
            && sha256.Length == 64
            && sha256.All(Uri.IsHexDigit);
    }
}
