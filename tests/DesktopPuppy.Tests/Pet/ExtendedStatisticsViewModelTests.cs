using DesktopPuppy.Pet.Statistics;
using DesktopPuppy.Usage;
using Xunit;
namespace DesktopPuppy.Tests.Pet;
public sealed class ExtendedStatisticsViewModelTests
{
    [Fact]
    public async Task OptionalFactsRefreshWithSelectedDateAndExposeCategoryShares()
    {
        var now=DateTimeOffset.Parse("2026-10-09T12:00:00Z");
        var vm=new UsageStatisticsViewModel(new Query(),TimeZoneInfo.Utc,()=>now,date=>Task.FromResult(new UsageFacts(date,now,TimeSpan.FromHours(2),3,TimeSpan.FromHours(1),TimeSpan.FromMinutes(30),new Dictionary<int,TimeSpan>(),new Dictionary<string,TimeSpan>{{"Work",TimeSpan.FromHours(1)},{"Entertainment",TimeSpan.Zero},{"Unclassified",TimeSpan.FromHours(1)}},[],new Dictionary<string,int>{{"Click",2}},4,1,2,3)));
        await vm.LoadAsync();
        Assert.Equal(3,vm.Facts!.ApplicationSwitchCount);
        Assert.Contains("工作 50%",vm.CategorySummary);
        Assert.Contains("成功 4",vm.AiSummary);
        await vm.SelectDateAsync(new(2026,10,8));
        Assert.Equal(new DateOnly(2026,10,8),vm.Facts.LocalDate);
    }
    private sealed class Query:IUsageStatisticsQuery
    { public Task<DailyUsageStatistics> QueryAsync(DateOnly date,CancellationToken cancellationToken=default)=>Task.FromResult(new DailyUsageStatistics(date,TimeSpan.Zero,[])); }
}
