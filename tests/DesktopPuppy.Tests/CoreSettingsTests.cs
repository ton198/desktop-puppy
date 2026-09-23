using DesktopPuppy.Core.Clock;
using DesktopPuppy.Core.Settings;
using Xunit;

namespace DesktopPuppy.Tests;

public sealed class CoreSettingsTests
{
    [Fact]
    public void Defaults_match_offline_desktop_puppy_policy()
    {
        var settings = new AppSettings();

        Assert.Equal(TimeSpan.FromMinutes(20), settings.Activity.GlobalCooldown);
        Assert.Null(settings.Activity.DailyActivityLimit);
        Assert.Equal(new TimeOnly(23, 0), settings.Activity.QuietHoursStart);
        Assert.Equal(new TimeOnly(8, 0), settings.Activity.QuietHoursEnd);
        Assert.True(settings.Statistics.Enabled);
        Assert.False(settings.Interaction.SoundEnabled);
        Assert.True(settings.Interaction.AllowHit);
        Assert.True(settings.Ai.Enabled);
        Assert.Equal("DeepSeek", settings.Ai.Provider);
        Assert.Equal("deepseek-flash", settings.Ai.Model);
        Assert.Null(settings.Ai.DailyRequestLimit);
    }

    [Fact]
    public void Clock_contract_can_be_replaced_by_a_test_clock()
    {
        var expected = new DateTimeOffset(2026, 9, 23, 12, 34, 56, TimeSpan.Zero);
        IClock clock = new FixedClock(expected);

        Assert.Equal(expected, clock.UtcNow);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
