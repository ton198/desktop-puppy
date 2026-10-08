using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using DesktopPuppy.Ai;

namespace DesktopPuppy.Security;

public interface IApiKeyProtector { byte[] Protect(string plaintext); string Unprotect(byte[] ciphertext); }
public sealed class ApiKeyUnavailableException() : Exception("API key is unavailable.");
public sealed class ApiKeyService(AiSqliteStore store, IApiKeyProtector protector)
{
    public bool IsConfigured(AiProvider provider) => store.ReadKey(provider) is { Length: > 0 };
    public void Save(AiProvider provider, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 4096 || key.Any(char.IsControl)) throw new ApiKeyUnavailableException();
        try { store.WriteKey(provider, protector.Protect(key.Trim())); } catch { throw new ApiKeyUnavailableException(); }
    }
    public string? Get(AiProvider provider)
    {
        try { var blob = store.ReadKey(provider); return blob is null ? null : protector.Unprotect(blob); } catch { throw new ApiKeyUnavailableException(); }
    }
    public void Delete(AiProvider provider) => store.DeleteKey(provider);
}

public sealed class DpapiApiKeyProtector : IApiKeyProtector
{
    public byte[] Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try { return Transform(bytes, true); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public string Unprotect(byte[] ciphertext)
    {
        var bytes = Transform(ciphertext, false);
        try { return Encoding.UTF8.GetString(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private static byte[] Transform(byte[] input, bool protect)
    {
        var source = new Blob { Length = input.Length, Data = Marshal.AllocHGlobal(input.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(input, 0, source.Data, input.Length);
            var ok = protect ? CryptProtectData(ref source, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) : CryptUnprotectData(ref source, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new ApiKeyUnavailableException();
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, result.Length); return result;
        }
        finally
        {
            for (int i = 0; i < source.Length; i++) Marshal.WriteByte(source.Data, i, 0);
            Marshal.FreeHGlobal(source.Data);
            if (output.Data != IntPtr.Zero) { for (int i = 0; i < output.Length; i++) Marshal.WriteByte(output.Data, i, 0); LocalFree(output.Data); }
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
