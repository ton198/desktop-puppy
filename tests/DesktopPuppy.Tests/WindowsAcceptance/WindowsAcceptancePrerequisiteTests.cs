using DesktopPuppy.WindowsAcceptance;
using Xunit;

namespace DesktopPuppy.Tests.WindowsAcceptance;

public sealed class WindowsAcceptancePrerequisiteTests
{
    // Purpose: never show a fixture in an unattended or remote session.
    // Coverage: absent opt-in, non-interactive, remote, and local interactive states.
    // Target effect: only an opted-in local interactive session passes the shared fixture gate.
    [Theory]
    [InlineData(null, true, 0, true)]
    [InlineData("0", true, 0, true)]
    [InlineData("1", false, 0, true)]
    [InlineData("1", true, 1, true)]
    [InlineData("1", true, 0, false)]
    public void Fixture_session_gate_rejects_unmet_prerequisites(string? optIn, bool interactive, int remoteMetric, bool shouldSkip)
    {
        Assert.Equal(shouldSkip, WindowsAcceptancePrerequisite.SessionSkipReason(optIn, interactive, remoteMetric) is not null);
    }
}
