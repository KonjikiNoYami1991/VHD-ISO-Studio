using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace VhdIsoStudio.Core;

/// <summary>Verifica Authenticode (WinVerifyTrust) e che il firmatario sia Microsoft.</summary>
public static class SignatureVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;          // 2 = WTD_UI_NONE
        public uint fdwRevocationChecks; // 0 = WTD_REVOKE_NONE
        public uint dwUnionChoice;       // 1 = WTD_CHOICE_FILE
        public IntPtr pFile;
        public uint dwStateAction;       // 0 = WTD_STATEACTION_IGNORE
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid pgActionID, IntPtr pWVTData);

    public static bool IsTrustedMicrosoftFile(string path, out string detail)
    {
        detail = "";
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = path
        };
        IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        IntPtr pData = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_DATA>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,
                fdwRevocationChecks = 0,
                dwUnionChoice = 1,
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0
            };
            Marshal.StructureToPtr(data, pData, false);
            int hr = WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, pData);
            if (hr != 0)
            {
                detail = $"WinVerifyTrust 0x{hr:X8}";
                return false;
            }
        }
        finally
        {
            Marshal.DestroyStructure<WINTRUST_FILE_INFO>(pFile);
            Marshal.FreeHGlobal(pFile);
            Marshal.FreeHGlobal(pData);
        }

        try
        {
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            string subject = cert.Subject;
            detail = subject;
            return subject.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            detail = ex.Message;
            return false;
        }
    }
}
