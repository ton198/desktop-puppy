namespace DesktopPuppy.Pet.Tray;

internal static class TrayResourceCleanup
{
    private const string CleanupFailureDiagnostic = "Tray resource cleanup step failed.";

    internal static void Run(params Action[] cleanupSteps) =>
        Run(TrayDiagnostics.WriteFixed, cleanupSteps);

    internal static void Run(Action<string> diagnosticSink, params Action[] cleanupSteps)
    {
        ArgumentNullException.ThrowIfNull(diagnosticSink);
        ArgumentNullException.ThrowIfNull(cleanupSteps);
        foreach (var cleanupStep in cleanupSteps)
        {
            try
            {
                cleanupStep();
            }
            catch
            {
                TrayDiagnostics.WriteSafely(diagnosticSink, CleanupFailureDiagnostic);
            }
        }
    }
}
