namespace DesktopPuppy.Pet.Tray;

internal static class TrayResourceCleanup
{
    private const string CleanupFailureDiagnostic = "Tray resource cleanup step failed.";

    internal static void Run(params Action[] cleanupSteps)
    {
        ArgumentNullException.ThrowIfNull(cleanupSteps);
        foreach (var cleanupStep in cleanupSteps)
        {
            try
            {
                cleanupStep();
            }
            catch
            {
                Console.Error.WriteLine(CleanupFailureDiagnostic);
            }
        }
    }
}
