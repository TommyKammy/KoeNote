using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace KoeNote.App.Services.Llm;

public sealed class LlamaRuntimePathBridge : IDisposable
{
    private const long MaxFallbackCopyBytes = 128L * 1024L * 1024L;
    private readonly string _directory;

    private LlamaRuntimePathBridge(string directory, string modelPath)
    {
        _directory = directory;
        ModelPath = modelPath;
    }

    public string ModelPath { get; }

    public static LlamaRuntimePathBridge Create(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("Review model not found.", modelPath);
        }

        var bridgeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "KoeNote",
            "runtime-bridge");
        var bridgeDirectory = Path.Combine(bridgeRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bridgeDirectory);

        var safeModelPath = Path.Combine(bridgeDirectory, "model" + Path.GetExtension(modelPath));
        TryCreateModelLink(modelPath, safeModelPath);
        return new LlamaRuntimePathBridge(bridgeDirectory, safeModelPath);
    }

    public static bool IsBridgePreparationException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or Win32Exception;
    }

    public string AddInputFile(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Llama runtime input file not found.", sourcePath);
        }

        var safeFileName = $"input-{Guid.NewGuid():N}{Path.GetExtension(sourcePath)}";
        var safePath = Path.Combine(_directory, safeFileName);
        File.Copy(sourcePath, safePath, overwrite: false);
        return safePath;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryCreateModelLink(string sourcePath, string linkPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (CreateHardLink(linkPath, sourcePath, IntPtr.Zero))
            {
                return;
            }

            var error = Marshal.GetLastWin32Error();
            FallbackCopySmallModelOrThrow(sourcePath, linkPath, new Win32Exception(error).Message);
            return;
        }

        FallbackCopySmallModelOrThrow(sourcePath, linkPath, "Hard links are only implemented for Windows llama runtime bridging.");
    }

    private static void FallbackCopySmallModelOrThrow(string sourcePath, string linkPath, string reason)
    {
        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length <= MaxFallbackCopyBytes)
        {
            File.Copy(sourcePath, linkPath, overwrite: false);
            return;
        }

        throw new IOException($"Could not create ASCII-safe model path for llama runtime: {reason}");
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateHardLink(
        string lpFileName,
        string lpExistingFileName,
        IntPtr lpSecurityAttributes);
}
