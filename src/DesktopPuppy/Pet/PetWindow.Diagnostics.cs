using System.Text.Json;
using System.Windows.Automation;
namespace DesktopPuppy.Pet;
public partial class PetWindow
{
    private readonly bool _diagnosticsEnabled = Environment.GetEnvironmentVariable("DESKTOP_PUPPY_TEST_DIAGNOSTICS") == "1";
    private long _diagnosticTicks, _diagnosticStationaryRun, _diagnosticMaximumStationaryRun;
    private void RecordAnimationTickDiagnostics(bool stationary)
    {
        if (!_diagnosticsEnabled) return;
        _diagnosticTicks++;
        _diagnosticStationaryRun = stationary ? _diagnosticStationaryRun + 1 : 0;
        _diagnosticMaximumStationaryRun = Math.Max(_diagnosticMaximumStationaryRun, _diagnosticStationaryRun);
    }
    private void PublishAnimationDiagnostics()
    {
        if (!_diagnosticsEnabled) return;
        AutomationProperties.SetHelpText(PetImage, JsonSerializer.Serialize(new
        { totalTicks = _diagnosticTicks, maxConsecutiveStationaryTicks = _diagnosticMaximumStationaryRun }));
        _diagnosticStationaryRun = 0;
    }
}
