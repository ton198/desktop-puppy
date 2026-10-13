using DesktopPuppy.Activities;
using DesktopPuppy.Reports;
using DesktopPuppy.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DesktopPuppy.Tests.Settings;

public sealed class ModuleSchemaVersionTests
{
    [Theory]
    [InlineData("activity_schema_version")]
    [InlineData("report_schema_version")]
    [InlineData("placement_schema_version")]
    public async Task Future_module_version_rejects_before_creating_tables(string marker)
    {
        var path=Path.Combine(Path.GetTempPath(),"puppy-schema-"+Guid.NewGuid()+".db");
        SQLitePCL.Batteries_V2.Init();
        try
        {
            using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();using var q=c.CreateCommand();
                q.CommandText=$"CREATE TABLE {marker}(id INTEGER PRIMARY KEY,version INTEGER NOT NULL); INSERT INTO {marker} VALUES(1,99)";q.ExecuteNonQuery();
            }
            if(marker=="activity_schema_version") Assert.Throws<InvalidOperationException>(()=>new ActivityStateStore(path));
            else if(marker=="report_schema_version") Assert.Throws<InvalidOperationException>(()=>new DailyReportStore(path));
            else await Assert.ThrowsAsync<InvalidOperationException>(()=>new PetPlacementStore(path).LoadAsync());
            using var read=new SqliteConnection($"Data Source={path};Pooling=False");read.Open();using var query=read.CreateCommand();
            query.CommandText="SELECT COUNT(*) FROM sqlite_master WHERE type='table'";Assert.Equal(1L,query.ExecuteScalar());
            query.CommandText=$"SELECT version FROM {marker}";Assert.Equal(99L,query.ExecuteScalar());
        }
        finally {File.Delete(path);}
    }

    [Fact]
    public async Task Legacy_placement_is_versioned_without_losing_position()
    {
        var path=Path.Combine(Path.GetTempPath(),"puppy-schema-"+Guid.NewGuid()+".db");
        SQLitePCL.Batteries_V2.Init();
        try
        {
            using(var c=new SqliteConnection($"Data Source={path};Pooling=False"))
            {
                c.Open();using var q=c.CreateCommand();q.CommandText="CREATE TABLE pet_placement(id INTEGER PRIMARY KEY,monitor_id TEXT NOT NULL,left_dip REAL NOT NULL,top_dip REAL NOT NULL,dock_side TEXT NOT NULL); INSERT INTO pet_placement VALUES(1,'DISPLAY',10,20,'Right')";q.ExecuteNonQuery();
            }
            Assert.Equal(new SavedPetPlacement("DISPLAY",10,20,"Right"),await new PetPlacementStore(path).LoadAsync());
            using var read=new SqliteConnection($"Data Source={path};Pooling=False");read.Open();using var query=read.CreateCommand();query.CommandText="SELECT version FROM placement_schema_version";Assert.Equal(1L,query.ExecuteScalar());
        }
        finally {File.Delete(path);}
    }
}
