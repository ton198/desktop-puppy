using System.Diagnostics;

namespace DesktopPuppy.Pet.Tray;

internal static class TrayDiagnostics
{
    internal static void WriteFixed(string diagnostic)
    {
        try
        {
            Console.Error.WriteLine(diagnostic);
        }
        catch
        {
        }

        try
        {
            Debug.WriteLine(diagnostic);
        }
        catch
        {
        }
    }

    internal static void WriteSafely(Action<string> diagnosticSink, string diagnostic)
    {
        try
        {
            diagnosticSink(diagnostic);
        }
        catch
        {
        }
    }
}