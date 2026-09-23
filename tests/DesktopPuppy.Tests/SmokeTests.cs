using Xunit;

namespace DesktopPuppy.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void ProductName_is_desktop_puppy()
    {
        Assert.Equal("Desktop Puppy", DesktopPuppy.ApplicationInfo.ProductName);
    }
}
