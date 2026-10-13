using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DesktopPuppy.Native;
public static class InstanceScope
{
    public static string ForDatabase(string? overridePath) => string.IsNullOrWhiteSpace(overridePath) ? "DesktopPuppy" :
        "DesktopPuppy.Test." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(overridePath).ToUpperInvariant())))[..24];
}
