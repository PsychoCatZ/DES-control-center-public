using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DesControlCenter.Infrastructure.Notifications;

/// <summary>
/// Windows DPAPI (CryptProtectData / CryptUnprotectData) called directly: data encrypted for the
/// current Windows user on this PC, decryptable only by that user here. Used so the Telegram bot
/// token never sits on disk in plain text. No extra NuGet package needed.
/// </summary>
internal static class Dpapi
{
    private const uint CryptProtectUiForbidden = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int cbData;
        public IntPtr pbData;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DataBlob dataIn, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DataBlob dataIn, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out DataBlob dataOut);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr handle);

    public static byte[] Protect(byte[] plain) => Run(plain, protect: true);

    public static byte[] Unprotect(byte[] encrypted) => Run(encrypted, protect: false);

    private static byte[] Run(byte[] input, bool protect)
    {
        var handle = GCHandle.Alloc(input, GCHandleType.Pinned);
        try
        {
            var blobIn = new DataBlob { cbData = input.Length, pbData = handle.AddrOfPinnedObject() };
            var ok = protect
                ? CryptProtectData(ref blobIn, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var blobOut)
                : CryptUnprotectData(ref blobIn, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out blobOut);
            if (!ok)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[blobOut.cbData];
                Marshal.Copy(blobOut.pbData, result, 0, blobOut.cbData);
                return result;
            }
            finally
            {
                LocalFree(blobOut.pbData);
            }
        }
        finally
        {
            handle.Free();
        }
    }
}
