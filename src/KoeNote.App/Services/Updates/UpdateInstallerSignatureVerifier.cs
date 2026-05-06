using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace KoeNote.App.Services.Updates;

public sealed record UpdateInstallerSignatureVerificationResult(
    string InstallerPath,
    string SignerSubject,
    DateTimeOffset VerifiedAt);

public interface IUpdateInstallerSignatureVerifier
{
    UpdateInstallerSignatureVerificationResult Verify(string installerPath);
}

public sealed record UpdateInstallerSignatureOptions(string? ExpectedSignerSubjectContains)
{
    public static UpdateInstallerSignatureOptions FromEnvironment()
    {
        return new UpdateInstallerSignatureOptions(
            Environment.GetEnvironmentVariable("KOENOTE_UPDATE_SIGNER_SUBJECT_CONTAINS"));
    }
}

public sealed class AuthenticodeUpdateInstallerSignatureVerifier(
    UpdateInstallerSignatureOptions? options = null) : IUpdateInstallerSignatureVerifier
{
    private readonly UpdateInstallerSignatureOptions _options = options ?? UpdateInstallerSignatureOptions.FromEnvironment();

    public UpdateInstallerSignatureVerificationResult Verify(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            throw new ArgumentException("Installer path is required.", nameof(installerPath));
        }

        var fullPath = Path.GetFullPath(installerPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Update installer was not found.", fullPath);
        }

        VerifyWinTrust(fullPath);
        // CreateFromSignedFile is still the framework API that extracts the signer cert from Authenticode payloads.
#pragma warning disable SYSLIB0057
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(fullPath));
#pragma warning restore SYSLIB0057
        if (string.IsNullOrWhiteSpace(certificate.Subject))
        {
            throw new InvalidOperationException("Update installer signature has no signer subject.");
        }

        if (!string.IsNullOrWhiteSpace(_options.ExpectedSignerSubjectContains) &&
            !certificate.Subject.Contains(_options.ExpectedSignerSubjectContains, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Update installer signature signer does not match the expected publisher.");
        }

        return new UpdateInstallerSignatureVerificationResult(fullPath, certificate.Subject, DateTimeOffset.Now);
    }

    private static void VerifyWinTrust(string path)
    {
        var fileInfo = new WinTrustFileInfo(path);
        var data = new WinTrustData(fileInfo);
        try
        {
            var action = WinTrustActionGenericVerifyV2;
            var result = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            if (result != 0)
            {
                var detail = new Win32Exception(result).Message;
                throw new InvalidOperationException(
                    $"Update installer signature verification failed: 0x{result:X8}. {detail}");
            }
        }
        finally
        {
            data.Dispose();
        }
    }

    private static readonly Guid WinTrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [DllImport("wintrust.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionId, ref WinTrustData pWVTData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustFileInfo
    {
        private uint _cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)]
        private string _pcwszFilePath;
        private IntPtr _hFile;
        private IntPtr _pgKnownSubject;

        public WinTrustFileInfo(string filePath)
        {
            _cbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>();
            _pcwszFilePath = filePath;
            _hFile = IntPtr.Zero;
            _pgKnownSubject = IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WinTrustData : IDisposable
    {
        private uint _cbStruct;
        private IntPtr _pPolicyCallbackData;
        private IntPtr _pSIPClientData;
        private uint _dwUIChoice;
        private uint _fdwRevocationChecks;
        private uint _dwUnionChoice;
        private IntPtr _pFile;
        private uint _dwStateAction;
        private IntPtr _hWVTStateData;
        private IntPtr _pwszURLReference;
        private uint _dwProvFlags;
        private uint _dwUIContext;

        public WinTrustData(WinTrustFileInfo fileInfo)
        {
            _cbStruct = (uint)Marshal.SizeOf<WinTrustData>();
            _pPolicyCallbackData = IntPtr.Zero;
            _pSIPClientData = IntPtr.Zero;
            _dwUIChoice = 2;
            _fdwRevocationChecks = 0;
            _dwUnionChoice = 1;
            _pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, _pFile, false);
            _dwStateAction = 0;
            _hWVTStateData = IntPtr.Zero;
            _pwszURLReference = IntPtr.Zero;
            _dwProvFlags = 0x00000010;
            _dwUIContext = 0;
        }

        public void Dispose()
        {
            if (_pFile != IntPtr.Zero)
            {
                Marshal.DestroyStructure<WinTrustFileInfo>(_pFile);
                Marshal.FreeHGlobal(_pFile);
                _pFile = IntPtr.Zero;
            }
        }
    }
}
