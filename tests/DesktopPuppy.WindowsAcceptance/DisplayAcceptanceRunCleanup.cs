namespace DesktopPuppy.WindowsAcceptance;

internal static class DisplayAcceptanceRunCleanup
{
    internal static async Task CleanupAsync(
        Func<Task<bool>> stopOwnedApp,
        Action deleteOwnedDirectory,
        Action disposeScenario)
    {
        try
        {
            var processExited = await stopOwnedApp();
            if (!processExited)
                throw new InvalidOperationException("The owned display acceptance app may still be running; its run directory was retained.");
            deleteOwnedDirectory();
        }
        finally
        {
            disposeScenario();
        }
    }
}
