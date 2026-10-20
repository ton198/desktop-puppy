using System.IO;

namespace DesktopPuppy.Runtime;

internal static class StartupDiagnostics
{
    private static readonly object Gate = new();

    // Callers supply fixed lifecycle labels, numeric window metadata and exception types.
    // Never include exception messages, window titles, database contents or API keys.
    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var database = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_USAGE_DB_PATH");
                var directory = string.IsNullOrWhiteSpace(database)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPuppy")
                    : Path.GetDirectoryName(Path.GetFullPath(database))!;
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "startup.log");
                if (File.Exists(path) && new FileInfo(path).Length > 256 * 1024)
                    File.Move(path, path + ".previous", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        }
        catch { /* Diagnostics must never prevent startup or visibility recovery. */ }
    }
}
