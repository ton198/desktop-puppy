using DesktopPuppy.Native;
using Xunit;

namespace DesktopPuppy.Tests.Native;
public sealed class InstanceScopeTests
{
    [Fact]
    public void Private_databases_do_not_activate_normal_user_pet()
    {
        var normal = InstanceScope.ForDatabase(null);
        var first = InstanceScope.ForDatabase(Path.GetFullPath("fixture-a.db"));
        var again = InstanceScope.ForDatabase(Path.GetFullPath("fixture-a.db").ToUpperInvariant());
        var second = InstanceScope.ForDatabase(Path.GetFullPath("fixture-b.db"));
        Assert.NotEqual(normal, first);
        Assert.Equal(first, again);
        Assert.NotEqual(first, second);
    }
}
