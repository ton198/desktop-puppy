using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DesktopPuppy.WindowsAcceptance;

internal static class WindowsAcceptancePrerequisite
{
    private const int SmRemoteSession = 0x1000;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    internal static string? SessionSkipReason() => SessionSkipReason(
        Environment.GetEnvironmentVariable("DESKTOP_PUPPY_WINDOWS_ACCEPTANCE"),
        Environment.UserInteractive,
        GetSystemMetrics(SmRemoteSession));

    internal static string? SessionSkipReason(string? optIn, bool userInteractive, int remoteSessionMetric)
    {
        if (optIn != "1")
            return "Opt-in is not enabled (DESKTOP_PUPPY_WINDOWS_ACCEPTANCE must be 1).";
        if (!userInteractive || remoteSessionMetric != 0)
            return "A local interactive Windows session is required.";
        return null;
    }
    internal static string? SkipReason()
    {
        var sessionReason = SessionSkipReason();
        if (sessionReason is not null) return sessionReason;
        var executable = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_EXE");
        if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) ||
            !string.Equals(Path.GetFileName(executable), "DesktopPuppy.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(executable))
            return "DESKTOP_PUPPY_EXE must point to an existing absolute DesktopPuppy.exe file.";
        try
        {
            var processes = Process.GetProcessesByName("DesktopPuppy");
            try
            {
                if (processes.Length != 0)
                    return "DesktopPuppy is already running; leave it untouched and close it before an isolated acceptance run.";
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return "Could not safely determine whether DesktopPuppy is already running.";
        }
        return null;
    }
}
